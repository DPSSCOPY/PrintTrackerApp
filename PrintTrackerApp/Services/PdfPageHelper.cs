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
                        
                        // Define the body area (ignoring top 15% and bottom 15% for headers/footers)
                        double margin = page.Height * 0.15;
                        double bodyTop = page.Height - margin;
                        double bodyBottom = margin;

                        bool hasBodyContent = false;

                        // Check if there is any visible text in the body area
                        foreach (var letter in page.Letters)
                        {
                            if (!string.IsNullOrWhiteSpace(letter.Value))
                            {
                                // PdfPig coordinates: (0,0) is bottom-left
                                if (letter.Location.Y > bodyBottom && letter.Location.Y < bodyTop)
                                {
                                    hasBodyContent = true;
                                    break;
                                }
                            }
                        }

                        // Check if there are any images in the body area
                        if (!hasBodyContent)
                        {
                            foreach (var image in page.GetImages())
                            {
                                double centerY = image.Bounds.Bottom + (image.Bounds.Height / 2);
                                if (centerY > bodyBottom && centerY < bodyTop)
                                {
                                    hasBodyContent = true;
                                    break;
                                }
                            }
                        }

                        // If it has content in the body, we will print it
                        if (hasBodyContent)
                        {
                            pagesToPrint.Add(i);
                        }
                    }
                    
                    if (pagesToPrint.Count == document.NumberOfPages)
                    {
                        isAllPages = true;
                    }
                    actualPageCount = pagesToPrint.Count;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading PDF with PdfPig: {ex.Message}");
                // Fallback: If error, return empty string or null, but we'll return empty to indicate failure or no pages.
                // Alternatively, return "1" to print at least the first page. Let's return empty to skip safely, or better, 
                // just return a format that prints all pages. If we want to be safe and print all pages on failure, we'd do:
                // return ""; 
                // Wait, if we return empty, we might skip the whole document. Let's just return empty on error and let the caller handle it.
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
