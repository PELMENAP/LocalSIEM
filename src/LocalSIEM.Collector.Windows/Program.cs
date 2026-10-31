using System.Threading.Channels;
using LocalSIEM.Collector.Windows;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "LocalSIEM Collector");
builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection(CollectorOptions.Section));
var capacity = builder.Configuration.GetValue($"{CollectorOptions.Section}:BufferCapacity", 20_000);
builder.Services.AddSingleton(Channel.CreateBounded<CollectedEvent>(new BoundedChannelOptions(capacity)
{
    SingleReader = true,
    FullMode = BoundedChannelFullMode.DropWrite
}));
builder.Services.AddHttpClient<EventForwarder>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<ForwarderService>();
if (OperatingSystem.IsWindows())
{
    builder.Services.AddHostedService<SecurityLogWatcher>();
}

var host = builder.Build();
if (!OperatingSystem.IsWindows())
{
    host.Services.GetRequiredService<ILogger<Program>>()
        .LogCritical("Windows Event Collector работает только в Windows — журнал событий недоступен на этой ОС");
    return 1;
}
await host.RunAsync();
return 0;
