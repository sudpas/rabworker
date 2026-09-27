using RabWorker.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddFormWorker(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
