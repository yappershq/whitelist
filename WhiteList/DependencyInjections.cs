using Microsoft.Extensions.DependencyInjection;
using WhiteList.Managers;
using WhiteList.Modules;

namespace WhiteList;

internal static class DependencyInjections
{
    extension(IServiceCollection services)
    {
        private void ImplSingleton<TService1, TService2, TImpl>()
            where TImpl : class, TService1, TService2
            where TService1 : class
            where TService2 : class
        {
            services.AddSingleton<TImpl>();

            services.AddSingleton<TService1>(x => x.GetRequiredService<TImpl>());
            services.AddSingleton<TService2>(x => x.GetRequiredService<TImpl>());
        }

        public void AddManagerDi()
        {
            services.ImplSingleton<IConfigManager, IManager, ConfigManager>();
            services.ImplSingleton<IPlayerManager, IManager, PlayerManager>();
        }

        public void AddModuleDi()
        {
            services.AddSingleton<IModule, CommandModule>();
        }
    }
}
