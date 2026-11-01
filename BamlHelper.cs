using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Reflection;

namespace EndermanQa.Desktop.XamlLoader
{
    public static class BamlHelper
    {
        public static void LoadBaml(object root, string resourceKey)
        {
            var asm = typeof(BamlHelper).Assembly;
            using (var stream = asm.GetManifestResourceStream("EndermanQa.Desktop.g.resources"))
            {
                if (stream == null)
                    throw new InvalidOperationException("EndermanQa.Desktop.g.resources not found");

                using (var reader = new System.Resources.ResourceReader(stream))
                {
                    string resType;
                    byte[] data;
                    reader.GetResourceData(resourceKey.ToLowerInvariant(), out resType, out data);
                    if (data == null)
                        throw new InvalidOperationException("Key not found in resources: " + resourceKey);

                    // Skip the 4-byte length prefix from ResourceReader
                    int offset = 4;
                    using (var ms = new MemoryStream(data, offset, data.Length - offset))
                    {
                        var baml2006ReaderType = typeof(XamlReader).Assembly
                            .GetType("System.Windows.Baml2006.Baml2006Reader");
                        
                        var readerSettings = new System.Xaml.XamlReaderSettings
                        {
                            BaseUri = new Uri("pack://application:,,,/"),
                            LocalAssembly = asm
                        };
                        var baml2006Reader = Activator.CreateInstance(baml2006ReaderType, ms, readerSettings);
                        
                        var schemaCtx = baml2006ReaderType.GetProperty("SchemaContext").GetValue(baml2006Reader);
                        var settings = new System.Xaml.XamlObjectWriterSettings { RootObjectInstance = root };
                        var writer = new System.Xaml.XamlObjectWriter((System.Xaml.XamlSchemaContext)schemaCtx, settings);

                        var xamlServicesType = typeof(System.Xaml.XamlServices);
                        var transformMethod = xamlServicesType.GetMethod("Transform", new[] { typeof(System.Xaml.XamlReader), typeof(System.Xaml.XamlWriter) });
                        transformMethod.Invoke(null, new object[] { baml2006Reader, writer });
                    }
                }
            }
        }
    }
}
