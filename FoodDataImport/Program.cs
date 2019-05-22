using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Dal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PerekrestokShop;
using Shop;
using SqLiteDal;

namespace FoodDataImport
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();
            var logger = provider.GetService<ILogger<Program>>();
            Options options = null;
            Parser.Default.ParseArguments<Options>(args)
               .WithParsed(opts => options = opts)
               .WithNotParsed(opts => logger.LogError("Ошибка в аргументах"));
            if (options != null)
            {
                var dal = provider.GetService<IDal>();
                var dataImport = provider.GetService<DataImport>();
                try
                {
                    logger.LogInformation("Начинаем");
                    using (var connection = dal.GetConnection())
                    {
                        await dal.InitializeAsync(connection);
                    }

                    if (!options.SkipCategoryImport)
                    {
                        logger.LogInformation("Импортируем список продуктов");
                        await dataImport.ImportProductIdsAsync(new ConsoleProgress(logger), CancellationToken.None);
                    }

                    logger.LogInformation("Испортируем сами продукты");
                    await dataImport.ImportProductsDataAsync(new ConsoleProgress(logger), options.ImportNewAndErrorOnly,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogCritical("Фатальная ошибка", ex);
                }

                Console.WriteLine("Готово");
            }

            Console.ReadLine();
        }


        private static void ConfigureServices(IServiceCollection services)
        {
            // configure logging
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddNLog());

            // build config
            IConfiguration configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json", true, true)
               .Build();

            services.AddOptions()
               .Configure<ConnectionStrings>(configuration.GetSection("ConnectionStrings"))
               .Configure<PerekrestokShopOptions>(configuration.GetSection("Perekrestok"));
            services.AddScoped<PerekrestokShop.PerekrestokShop>();
            services.AddScoped<IDal, SqLiteDal.SqLiteDal>();
            services.AddScoped(c => new IShop[] {c.GetService<PerekrestokShop.PerekrestokShop>()});
            services.AddScoped<DataImport>();
        }

        private class Options
        {
            [Option('s', "skip-category", Default = false, HelpText = "Пропустить шаг импорта списка продуктов")]
            public bool SkipCategoryImport { get; set; }

            [Option('n', "import-new", Default = false,
                HelpText = "Импортировать данные только о новых и ошибочных продуктах")]
            public bool ImportNewAndErrorOnly { get; set; }
        }

        private class ConsoleProgress : Progress<double>
        {
            private readonly ILogger _logger;
            private int _previousValue = -1;

            public ConsoleProgress(ILogger logger)
            {
                _logger = logger;
            }

            protected override void OnReport(double value)
            {
                var newValue = (int) (value * 100);
                var pv = Interlocked.Exchange(ref _previousValue, newValue);
                if (newValue != pv)
                    _logger.LogInformation($"Прогресс: {newValue}%");
            }
        }
    }
}