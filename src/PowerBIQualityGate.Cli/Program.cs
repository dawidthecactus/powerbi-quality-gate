using PowerBIQualityGate.Cli;

var rootCommand = CliApplication.CreateRootCommand();

return rootCommand.Parse(args).Invoke();
