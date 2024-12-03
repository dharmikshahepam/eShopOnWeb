using Azure.Identity;
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Azure.Security.KeyVault.Secrets;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        bool useOnlyInMemoryDatabase = false;
        if (configuration["UseOnlyInMemoryDatabase"] != null)
        {
            useOnlyInMemoryDatabase = bool.Parse(configuration["UseOnlyInMemoryDatabase"]!);
        }

        if (useOnlyInMemoryDatabase)
        {
            services.AddDbContext<CatalogContext>(c =>
               c.UseInMemoryDatabase("Catalog"));

            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase("Identity"));
        }
        else
        {
            // use real database
            // Requires LocalDB which can be installed with SQL Server Express 2016
            // https://www.microsoft.com/en-us/download/details.aspx?id=54284
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));

            //services.AddDbContext<CatalogContext>(c =>
            //    c.UseSqlServer(GetConnectionString(configuration, "CatalogConnectionDB")));

            //// Add Identity DbContext
            //services.AddDbContext<AppIdentityDbContext>(options =>
            //    options.UseSqlServer(GetConnectionString(configuration, "IdentityConnectionDB")));
        }
    }

    private static string? GetConnectionString(IConfiguration configuration, string secretName)
    {
        var keyVaultURL = configuration.GetSection("KeyVault:KeyVaultURL");
        var keyVaultClientId = configuration.GetSection("KeyVault:ClientId");
        var keyVaultClientSecret = configuration.GetSection("KeyVault:ClientSecret");
        var keyVaultDirectoryId = configuration.GetSection("KeyVault:DirectoryId");

        var credential = new ClientSecretCredential(keyVaultDirectoryId?.Value, keyVaultClientId?.Value, keyVaultClientSecret?.Value);
        var client = new SecretClient(new Uri(keyVaultURL?.Value), new DefaultAzureCredential());
        var connectionString = client.GetSecret(secretName).Value.Value.ToString();
        return connectionString;
    }
}
