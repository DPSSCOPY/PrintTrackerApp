import re

def parse_filename(fullFileName):
    nameWithoutExt = fullFileName.rsplit('.', 1)[0]
    match = re.search(r'(\d+)[\s_-]*(?:copy|copies)', nameWithoutExt, re.IGNORECASE)
    
    if match:
        beforeCopies = nameWithoutExt[:match.start()]
        print("beforeCopies:", beforeCopies)
        
        beforeParts = [p for p in re.split(r'[-_]', beforeCopies) if p]
        print("beforeParts:", beforeParts)
        
        rawUserId = beforeParts[0] if beforeParts else "Default"
        rawFileName = "-".join(beforeParts[1:]) if len(beforeParts) > 1 else ""
        
        finalUserId = "".join(c for c in rawUserId if c.isalnum())
        if len(finalUserId) > 8: finalUserId = finalUserId[:8]
        
        finalFileName = "".join(c for c in rawFileName if c != '"' and c != '.')
        if len(finalFileName) > 16: finalFileName = finalFileName[:16]
        
        print("Match SUCCESS")
        print("finalUserId:", finalUserId)
        print("finalFileName:", finalFileName)
    else:
        print("Match FAILED")

parse_filename("L2-Chanpanha-S2-18copies(Review1) .pdf")
parse_filename("Pre5-Vornn-S1-16copies (Weekly Review 10).pdf")
