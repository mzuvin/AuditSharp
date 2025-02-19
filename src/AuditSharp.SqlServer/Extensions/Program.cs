using AuditSharp.EntityFrameworkCore.Context;
using AuditSharp.SqlServer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuditSharp.SqlServer.Extensions;

public static class Program
{
    public static IServiceCollection AddAuditSharp(this IServiceCollection services, Action<DbContextOptionsBuilder> optionsBuilder)
    {
        services.AddDbContext<AuditSharpSqlServerDbContext>(optionsBuilder, ServiceLifetime.Transient, ServiceLifetime.Transient);
        services.AddTransient<IAuditSharpContext>(sp=> sp.GetService<AuditSharpSqlServerDbContext>()!);
        services.Register();
        return services;
    }

    public static void RegisterAuditSharp(this DbContextOptionsBuilder options)
    {
        options.AddInterceptors(new Interceptor());
    }

    public static IHost UseAuditSharp(this IHost host)
    {
        using var serviceScope = host.Services.CreateScope();
        var context = serviceScope.ServiceProvider.GetService<AuditSharpSqlServerDbContext>();
        var pendingMigrations = context!.Database.GetPendingMigrations();
        if (pendingMigrations.Any())
        {
            context.Database.Migrate();
        }
        else
        {
            context.Database.EnsureCreated();
        }

        return host;
    }
}
