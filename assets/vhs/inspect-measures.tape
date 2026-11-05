# PBIQ 02.5.3.3: genuine CLI terminal screenshot.
# Execute only in a clean checkout containing the repository's public fixtures.
# Run from repository root after `dotnet build -c Release`.
# NEVER replace these commands with a private .pbip path.
Output assets/demos/.capture-inspect-measures.gif
Require dotnet
Set Shell "bash"
Set FontFamily "JetBrains Mono"
Set FontSize 19
Set Width 2040
Set Height 510
Set WindowBar Colorful
Set Padding 26
Set TypingSpeed 1ms
Set Theme {"name":"Cactus Obsidian","black":"#14201A","red":"#FF777D","green":"#82D89A","yellow":"#F4CF83","blue":"#89B5FF","magenta":"#C99BEE","cyan":"#80D5CA","white":"#E4EEE7","brightBlack":"#6B7C72","brightRed":"#FF9B9B","brightGreen":"#B1E8BB","brightYellow":"#FFE0A0","brightBlue":"#B2D0FF","brightMagenta":"#E3B9FF","brightCyan":"#B7EADF","brightWhite":"#FFFFFF","background":"#111A16","foreground":"#E0EDE4","selection":"#304638","cursor":"#9CEBB0"}

Hide
Type 'pbiq() { dotnet src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll "$@"; }'
Enter
Type "clear"
Enter
Show
Type "pbiq inspect examples/valid-pbir-tmdl --measures"
Enter
Wait+Screen@30s /Measures inspected: 2/2/
Sleep 700ms
Screenshot assets/screenshots/inspect-measures.png
Sleep 300ms
