using System;
using System.Linq;
using System.Reflection;
using WritableJsonConfiguration;

namespace Microsoft.Extensions.Configuration
{
    public static class WritableJsonConfigurationProviderExtensions
    {
        /// <summary>
        /// Set value for current section
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="value"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void Set(this IConfiguration configuration, object value)
        {
            switch (configuration)
            {
                case IConfigurationRoot configurationRoot:
                    {
                        foreach (var provider in configurationRoot.Providers)
                        {
                            if (provider is WritableJsonConfigurationProvider writableProvider)
                                writableProvider.Set(null, value);
                            else
                                provider.Set(null, value.ToString());
                        }
                        break;
                    }
                case ConfigurationSection configurationSection:
                    {
                        var rootProp = typeof(ConfigurationSection).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance);
                        var root = rootProp.GetValue(configurationSection) as IConfigurationRoot;
                        foreach (var provider in root.Providers)
                        {
                            if (provider is WritableJsonConfigurationProvider writableProvider)
                                writableProvider.Set(configurationSection.Path, value);
                            else
                                provider.Set(configurationSection.Path, value.ToString());
                        }
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(configuration));
            }
        }

        /// <summary>
        /// Get section and set value
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="section"></param>
        /// <param name="value"></param>
        public static void Set(this IConfiguration configuration, string section, object value)
        {
            configuration.GetSection(section).Set(value);
        }

        /// <summary>
        /// Get object by section
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="configuration"></param>
        /// <param name="section"></param>
        /// <returns></returns>
        public static T Get<T>(this IConfiguration configuration, string section)
        {
            return configuration.GetSection(section).Get<T>();
        }
    }
}