using Microsoft.Extensions.Configuration;
using Xunit.Sdk;

namespace WritableJsonConfiguration.Tests
{
    public class ConfigTests : IDisposable
    {
        readonly IConfigurationRoot configuration;

        public ConfigTests()
        {
            configuration = WritableJsonConfigurationFabric.Create("Settings.json");
        }

        public void Dispose()
        {
            var config = configuration.Providers.First() as WritableJsonConfigurationProvider;
            var fileName = config.Source.Path;
            File.Delete(fileName);
        }

        [Fact]
        public void ArrayTest()
        {
            
            var array = new string[] { "1", "a" };
            configuration.Set("array", array);
            var result = configuration.Get<string[]>("array");
            Assert.Equal(array, result);
            
        }

        [Fact]
        public void ArraySectionTest()
        {
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create("Settings.json");
            var array = new string[] { "1", "a" };
            configuration.GetSection("data").Set("array", array);
            var result = configuration.GetSection("data").Get<string[]>("array");
            Assert.Equal(array, result);
        }

    }
}