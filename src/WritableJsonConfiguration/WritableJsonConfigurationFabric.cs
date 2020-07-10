using System;
using Microsoft.Extensions.Configuration;

namespace WritableJsonConfiguration
{
    public static class WritableJsonConfigurationFabric
    {
        public static IConfigurationRoot Create(string path, bool reloadOnChange = true, bool optional = true)
        {
            void ConfigureSource(WritableJsonConfigurationSource s)
            {
                s.Path = path;
                s.Optional = optional;
                s.ReloadOnChange = reloadOnChange;
                s.ResolveFileProvider();
            }

            return Create(ConfigureSource);
        }

        public static IConfigurationRoot Create(Action<WritableJsonConfigurationSource> configureSource)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfigurationRoot configuration = configurationBuilder.Add<WritableJsonConfigurationSource>(configureSource).Build();
            return configuration;
        }
    }
}
