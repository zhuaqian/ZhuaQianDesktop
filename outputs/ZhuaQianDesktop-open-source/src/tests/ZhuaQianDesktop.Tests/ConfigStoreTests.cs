using System;
using Xunit;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktop.Tests
{
    public class ConfigStoreTests
    {
        private string _testDir;
        private ConfigStore _configStore;

        public ConfigStoreTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "zqtest_" + Guid.NewGuid().ToString("N"));
            _configStore = new ConfigStore(_testDir);
        }

        [Fact]
        public void Set_Get_WorksCorrectly()
        {
            _configStore.Set("model", "gemini-flash-lite-latest");
            
            var result = _configStore.Get<string>("model", "default");
            
            Assert.Equal("gemini-flash-lite-latest", result);
        }

        [Fact]
        public void Get_NonExistentKey_ReturnsFallback()
        {
            var result = _configStore.Get<string>("nonexistent", "fallback");
            
            Assert.Equal("fallback", result);
        }

        [Fact]
        public void Save_Load_PreservesData()
        {
            _configStore.Set("api_key", "test_key_123");
            _configStore.Save();
            
            var newStore = new ConfigStore(_testDir);
            newStore.Load();
            
            var loadedKey = newStore.Get<string>("api_key", "");
            
            Assert.Equal("test_key_123", loadedKey);
        }

        [Fact]
        public void Load_NonExistentFile_InitializedEmpty()
        {
            var result = _configStore.Get<int>("count", 0);
            
            Assert.Equal(0, result);
        }
    }
}
