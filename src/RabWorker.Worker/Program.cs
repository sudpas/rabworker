var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<RabWorker.Worker.FormWorker>();
await builder.Build().RunAsync();
