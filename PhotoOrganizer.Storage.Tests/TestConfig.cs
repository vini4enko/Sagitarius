using Microsoft.Extensions.Configuration;
using PhotoOrganizer.Storage;

namespace PhotoOrganizer.Storage.Tests;

public class TestConfig
{
    public S3Options S3 { get; init; } = new();
    public TestSettings Test { get; init; } = new();

    public static TestConfig Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var cfg = new TestConfig();
        configuration.GetSection("S3").Bind(cfg.S3);
        configuration.GetSection("Test").Bind(cfg.Test);
        return cfg;
    }
}

public class TestSettings
{
    public string AdminLogin { get; set; } = "admin";
    public string AdminPassword { get; set; } = "";
    public string TestObjectKey { get; set; } = "tests/photo1.jpg";
}