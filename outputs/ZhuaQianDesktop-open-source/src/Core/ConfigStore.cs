using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Core
{
    // Simple, csc-buildable configuration store. The unit tests exercise an
    // instance-based key/value API (ctor(dir) + Set/Get/Save/Load) backed by a
    // JSON file. This intentionally avoids System.Text.Json so it compiles with the
    // framework csc.exe that ships with .NET 4.8.
    public class ConfigStore
    {
        private string _path;
        private Dictionary<string, object> _data = new Dictionary<string, object>();

        public ConfigStore(string dir)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "config.json");
        }

        public void Set(string key, object value)
        {
            _data[key] = value;
        }

        public T Get<T>(string key, T fallback)
        {
            if (!_data.ContainsKey(key))
                return fallback;
            try
            {
                return (T)Convert.ChangeType(_data[key], typeof(T));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ConfigStore.Get(" + key + "): " + ex.Message);
                return fallback;
            }
        }

        public string Get(string key, string fallback)
        {
            if (!_data.ContainsKey(key))
                return fallback;
            return Convert.ToString(_data[key]);
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(_path, ser.Serialize(_data), Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ConfigStore.Save: " + _ex.Message); }
        }

        public void Load()
        {
            _data = new Dictionary<string, object>();
            if (!File.Exists(_path))
                return;
            try
            {
                var ser = new JavaScriptSerializer();
                var d = ser.DeserializeObject(File.ReadAllText(_path, Encoding.UTF8)) as Dictionary<string, object>;
                if (d != null)
                    _data = d;
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ConfigStore.Load: " + _ex.Message); }
        }
    }
}
