# PichalUI
Console UI Like app for Windows


# Como usar:
## Para usar sem compilar 

No terminal do VSCode ou cmd/powershell (como admin) fazer: 

`cd C:\path\ConsoleUI_WPF\` 

De seguida pôr a seguinte linha de código:

`dotnet run --project PichalUI`

## Para compilar:

No terminal do VSCode ou cmd/powershell (como admin) fazer: 

`cd C:\path\ConsoleUI_WPF\` 

De seguida pôr a seguinte linha de código:

`dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o ./publish`

# Adicionar Packages no Projeto

Para adicionar Packages no Projeto, o caminho a utilizar é:

`C:\path\ConsoleUI_WPF\PichalUI`

De Seguida adicionar a package, por exemplo:

`dotnet add package DualSenseAPI`

