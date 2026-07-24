using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;

namespace PrintTrackerApp.Services
{
    public static class PdfPageHelper
    {
        /// <summary>
        /// Reads PDF and returns a comma-separated string of non-blank page ranges.
        /// </summary>
        public static string GetNonBlankPagesString(string pdfFilePath, out bool isAllPages, out int actualPageCount)
        {
            List<int> pagesToPrint = new List<int>();
            isAllPages = false;
            actualPageCount = 0;

            try
            {
                using (PdfDocument document = PdfDocument.Open(pdfFilePath))
                {
                    for (int i = 1; i <= document.NumberOfPages; i++)
                    {
                        var page = document.GetPage(i);
                        
                        // Define the body area (ignoring top 10% and bottom 10% for headers/footers)
                        double margin = page.Height * 0.10;
                        double bodyTop = page.Height - margin;
                        double bodyBottom = margin;

                        bool hasBodyContent = false;

                        // 1. Check if there is any visible text in the body area
                        foreach (var letter in page.Letters)
                        {
                            if (!string.IsNullOrWhiteSpace(letter.Value))
                            {
                                // PdfPig coordinates: (0,0) is bottom-left
                                if (letter.Location.Y >= bodyBottom && letter.Location.Y <= bodyTop)
                                {
                                    hasBodyContent = true;
                                    break;
                                }
                            }
                        }

                        // 2. Check if there are any images overlapping the body area
                        if (!hasBodyContent)
                        {
                            try
                            {
                                foreach (var image in page.GetImages())
                                {
                                    // Check if image overlaps body area or is a large image (covers > 25% height)
                                    double imgTop = image.Bounds.Top;
                                    double imgBottom = image.Bounds.Bottom;
                                    double imgHeight = image.Bounds.Height;

                                    if ((imgTop >= bodyBottom && imgBottom <= bodyTop) ||
                                        (imgBottom < bodyTop && imgTop > bodyBottom) ||
                                        imgHeight > (page.Height * 0.25))
                                    {
                                        hasBodyContent = true;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }

                        // If it has content in the body, we will print it
                        if (hasBodyContent)
                        {
                            pagesToPrint.Add(i);
                        }
                    }
                    
                    // Safety Net: If ALL pages were detected as "blank", but total pages > 0,
                    // fallback to printing all pages so legitimate image/unrecognized documents are never lost.
                    if (pagesToPrint.Count == 0 && document.NumberOfPages > 0)
                    {
                        for (int i = 1; i <= document.NumberOfPages; i++) pagesToPrint.Add(i);
                        isAllPages = true;
                    }
                    else if (pagesToPrint.Count == document.NumberOfPages)
                    {
                        isAllPages = true;
                    }
                    
                    actualPageCount = pagesToPrint.Count;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading PDF with PdfPig: {ex.Message}");
                isAllPages = true;
            }

            return FormatPageRange(pagesToPrint);
        }

        /// <summary>
        /// Converts a list of integers into a page range string (e.g. 1-3, 5)
        /// </summary>
        private static string FormatPageRange(List<int> pages)
        {
            if (pages == null || !pages.Any()) return string.Empty;
            
            pages.Sort();
            var ranges = new List<string>();
            int start = pages[0];
            int end = pages[0];

            for (int i = 1; i < pages.Count; i++)
            {
                if (pages[i] == end + 1)
                {
                    end = pages[i];
                }
                else
                {
                    ranges.Add(start == end ? start.ToString() : $"{start}-{end}");
                    start = pages[i];
                    end = pages[i];
                }
            }
            ranges.Add(start == end ? start.ToString() : $"{start}-{end}");

            return string.Join(", ", ranges);
        }
    }
}
