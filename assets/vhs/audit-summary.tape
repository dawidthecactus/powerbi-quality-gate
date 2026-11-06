# PBIQ 02.5.3.3: genuine CLI screenshot on public synthetic fixtures.
# No customer PBIP, filenames, DAX or model paths are used here.
# CLI output and expected exit codes are verified BEFORE VHS recording in Actions.
# VHS v0.12.1 has an unreliable Wait+Screen matcher after terminal scrolling.
# Use a bounded 3s render settle, then visually inspect the resulting PNG.
Output assets/demos/.capture-audit-summary.gif
Require dotnet
Set Shell "bash"
Set FontFamily "JetBrains Mono"
Set FontSize 22
Set Width 1530
Set Height 830
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
Type "pbiq audit examples/valid-pbir-tmdl"
Enter
Sleep 3s
Screenshot assets/screenshots/audit-summary-color.png
Sleep 300ms
