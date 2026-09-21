using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace WritableJsonConfiguration
{
    public class WritableJsonConfigurationSource : JsonConfigurationSource
    {
        /// <summary>
        /// Use Windows atomic replacement and a previous-version .bak file.
        /// Disabled by default to preserve existing consumers and platforms.
        /// </summary>
        public bool UseAtomicWrites { get; set; }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            this.EnsureDefaults(builder);
            return (IConfigurationProvider)new WritableJsonConfigurationProvider(this);
        }
    }
}
