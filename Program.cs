using System.CommandLine;
using CnCNetServer;

using IHost app = Startup.BuildApplication(args);

await app.StartAsync().ConfigureAwait(ConfigureAwaitOptions.None);

ParseResult parseResult = app.Services.GetRequiredService<ParseResult>();

return await parseResult.InvokeAsync().ConfigureAwait(ConfigureAwaitOptions.None);