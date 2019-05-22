using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RestSharp;
using Shop;

namespace PerekrestokShop
{
    public class PerekrestokShop : IShop
    {
        private const int DownloadThreadCount = 10;
        private readonly RestClient _apiClient;
        private readonly RestClient _client;
        private readonly ILogger _logger;
        private readonly PerekrestokShopOptions _options;
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _networkLock = new SemaphoreSlim(DownloadThreadCount, DownloadThreadCount);
        private string _token;

        public PerekrestokShop(IOptions<PerekrestokShopOptions> options, ILogger<PerekrestokShop> logger)
        {
            _options = options.Value;
            _logger = logger;
            _apiClient = new RestClient(options.Value.ApiUrl);
            _client = new RestClient(options.Value.Url) {FollowRedirects = false};
            _token = options.Value.Token;
        }


        public async Task GetProductIds(Func<ProductId, Task> dataAction, IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            var catalogNames = new[]
            {
                "moloko-syr-yaytsa",
                "ovoschi-frukty-griby",
                "myaso-ptitsa-delikatesy",
                "zamorojennye-produkty",
                "makarony-krupy-spetsii",
                "zdorovyy-vybor",
                "hleb-sladosti-sneki",
                "ryba-i-moreprodukty",
                "kofe-chay-sahar",
                "soki-vody-napitki",
                "konservy-orehi-sousy",
                "alkogol",
                "tovary-dlya-jivotnyh",
                "krasota-gigiena-bytovaya-himiya",
                "tovary-dlya-mam-i-detey",
                "avto-dom-sad-kuhnya",
                "moloko-syr-yaytsa",
                "ovoschi-frukty-griby",
                "myaso-ptitsa-delikatesy",
                "zamorojennye-produkty",
                "makarony-krupy-spetsii",
                "zdorovyy-vybor",
                "hleb-sladosti-sneki",
                "ryba-i-moreprodukty",
                "kofe-chay-sahar",
                "soki-vody-napitki",
                "konservy-orehi-sousy",
                "alkogol",
                "tovary-dlya-jivotnyh",
                "krasota-gigiena-bytovaya-himiya",
                "tovary-dlya-mam-i-detey",
                "avto-dom-sad-kuhnya"
            };

            var currentProgress = 0;
            var catalogNamesAndPagesCount = await Task.WhenAll(catalogNames.Select(async catalogName =>
            {
                try
                {
                    return Tuple.Create(catalogName, await GetCategoryPagesCount(catalogName, cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Проблема в категории {catalogName}");
                    return Tuple.Create(catalogName, 0);
                }
            }));
            var pagesCount = catalogNamesAndPagesCount.Sum(catalogNameAndPagesCount => catalogNameAndPagesCount.Item2);
            await Task.WhenAll(catalogNamesAndPagesCount
               .Where(catalogNameAndPagesCount => catalogNameAndPagesCount.Item2 > 0)
               .Select(async catalogNameAndPagesCount =>
                {
                    await Task.WhenAll(Enumerable.Range(1, catalogNameAndPagesCount.Item2 - 1)
                       .Select(async page =>
                        {
                            try
                            {
                                var (_, productIds) = await GetProductIdsInCategory(catalogNameAndPagesCount.Item1,
                                    page,
                                    cancellationToken);
                                foreach (var productId in productIds)
                                {

                                    var p = new ProductId
                                    {
                                        ExternalId = productId,
                                        ShopType = ShopType,
                                        Status = ProductIdStatus.New,
                                        Updated = DateTime.UtcNow,
                                        Created = DateTime.UtcNow
                                    };
                                    await dataAction(p);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Проблема в категории {catalogNameAndPagesCount.Item1}");
                            }
                            finally
                            {
                                var p = Interlocked.Increment(ref currentProgress);
                                progress?.Report(p / (double)pagesCount);
                            }
                        }));
                }));
        }


        public async Task<FoodData> Import(int productId, CancellationToken cancellationToken)
        {
            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                _token = _token ?? await GetToken(cancellationToken);
            }
            finally
            {
                _tokenLock.Release();
            }


            return await ParseProduct(productId, _options.RegionId, _token, cancellationToken);
        }

        public ShopType ShopType => ShopType.Perekrestok;

        private static readonly Regex DecimalRegex = new Regex(@"(?i)(\s|^)(\d+\.?\d*|\.\d+)\s*(мл|л|г|кг|ккал)\s*$", RegexOptions.Compiled);
        private static (decimal?, FoodValueType) ParseDecimalValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return (null, FoodValueType.None);

            var match = DecimalRegex.Match(value);
            if (!match.Success)
                return (null, FoodValueType.None);

            var name = match.Groups[3].Value;
            var decimalValue = decimal.Parse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture);
            int mult;
            FoodValueType foodValueType;
            switch (name.ToLowerInvariant())
            {
                case "л":
                    mult = 1000;
                    foodValueType = FoodValueType.Volume;
                    break;
                case "мл":
                    mult = 1;
                    foodValueType = FoodValueType.Volume;
                    break;
                case "г":
                    mult = 1;
                    foodValueType = FoodValueType.Mass;
                    break;
                case "кг":
                    mult = 1000;
                    foodValueType = FoodValueType.Mass;
                    break;
                case "ккал":
                    mult = 1;
                    foodValueType = FoodValueType.Energy;
                    break;
                default:
                    return (null, FoodValueType.None);
            }

            return (mult * decimalValue, foodValueType);
        }

        private static FoodData ParseProduct(JObject product)
        {
            var quality = product["data"]["paramGroups"]
               .FirstOrDefault(pg => pg.Value<string>("name") == "Пищевая ценность на 100г");
            if (quality == null)
                return null;

            var summary = product["data"]["paramGroups"].FirstOrDefault(pg => pg.Value<string>("name") == "Описание");

            var name = product["data"].Value<string>("name").Trim();
            var qualityValues = quality["params"]
               .ToDictionary(q => q.Value<string>("name").Trim(), q => q.Value<string>("value"));
            var summaryValues =
                summary?["params"]
                  ?.Where(q => new[] {"Вес", "Объем"}.Contains(q.Value<string>("name")))
                   .ToDictionary(q => q.Value<string>("name").Trim(), q => q.Value<string>("value")) ??
                new Dictionary<string, string>();
            var (qFromName, fvFromName) = ParseDecimalValue(name);
            var (weightValue, weightType) =
                ParseDecimalValue(!summaryValues.ContainsKey("Вес") ? null : summaryValues["Вес"]);
            var (volumeValue, volumeType) =
                ParseDecimalValue(!summaryValues.ContainsKey("Объем") ? null : summaryValues["Объем"]);
            var (energyValue, energyType) = ParseDecimalValue(!qualityValues.ContainsKey("Энергетическая ценность")
                ? null
                : qualityValues["Энергетическая ценность"]);
            var isFractional = product["data"].Value<bool>("isFractional");
            return new FoodData
            {
                ShopType = ShopType.Perekrestok,
                ExternalId = product["data"].Value<int>("productId"),
                Category = product["data"].Value<string>("mainCategoryName"),
                Url = product["data"].Value<string>("productSiteUrl"),
                Name = name,
                Price = product["data"].Value<decimal>("price"),
                Weight =
                    (fvFromName != FoodValueType.Mass ? null : qFromName) ??
                    (weightType != FoodValueType.Mass ? null : weightValue) ??
                    (isFractional ? (decimal?) 1000.0m : null),
                Volume =
                    (fvFromName != FoodValueType.Volume ? null : qFromName) ??
                    (volumeType != FoodValueType.Volume ? null : volumeValue),
                Energy = energyType != FoodValueType.Energy ? null : energyValue,
                Proteins =
                    ParseDecimalValue(!qualityValues.ContainsKey("Белки") ? null : qualityValues["Белки"]).Item1,
                Fats = ParseDecimalValue(!qualityValues.ContainsKey("Жиры") ? null : qualityValues["Жиры"]).Item1,
                Carbohydrates =
                    ParseDecimalValue(!qualityValues.ContainsKey("Углеводы") ? null : qualityValues["Углеводы"])
                       .Item1,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };
        }

        private static readonly Regex ProductIdRegex = new Regex(@"xf-catalog__item\s*""\s*data-id=""(\d+)""", RegexOptions.Compiled);
        private static int[] ParseCategory(JObject product)
        {
            var html = product.Value<string>("html");
            var matches = ProductIdRegex.Matches(html
            );
            return matches.Select(match => int.Parse(match.Groups[1].Value)).ToArray();
        }

        //NOTE: если узнаете, как скачивать токен, пишите сюда 
        protected Task<string> GetToken(CancellationToken cancellationToken)
        {
            return Task.FromResult((string) null);
        }

        protected async Task<FoodData> ParseProduct(int productId, int regionId, string token,
            CancellationToken cancellationToken)
        {
            var request = new RestRequest("v5/store_products/product")
               .AddQueryParameter("productId", productId.ToString())
               .AddQueryParameter("regionId", regionId.ToString())
               .AddHeader("Content-Type", "application/json")
               .AddHeader("User-Agent",
                    "Perekrestok/2.6.0 (com.x5retailgroup.perekrestok-new; build:570; iOS 12.1.4) Alamofire/4.8.1")
               .AddHeader("X-Authorization", $"Bearer {token}");

            IRestResponse response;

            await _networkLock.WaitAsync(cancellationToken);
            try
            {
                response = await _apiClient.ExecuteGetTaskAsync(request, cancellationToken);
            }
            finally
            {
                _networkLock.Release();
            }

            if (!response.IsSuccessful)
                throw new HttpRequestException($"Проблема с {_apiClient.BuildUri(request)}", response.ErrorException);

            var jObject = JObject.Parse(response.Content);
            return ParseProduct(jObject);
        }

        protected async Task<(int totalCount, int[] productIds)> GetProductIdsInCategory(string categoryName, int page,
            CancellationToken cancellationToken)
        {
            var request = new RestRequest($"catalog/{categoryName}").AddQueryParameter("page", page.ToString())
               .AddQueryParameter("sort", "rate_desc")
               .AddQueryParameter("ajax", "true");

            IRestResponse response;
            await _networkLock.WaitAsync(cancellationToken);
            try
            {
                response = await _client.ExecuteGetTaskAsync(request, cancellationToken);
            }
            finally
            {
                _networkLock.Release();
            }

            if (!response.IsSuccessful)
                throw new HttpRequestException($"Проблема с {_client.BuildUri(request)}", response.ErrorException);

            var jObject = JObject.Parse(response.Content);
            var totalCount = jObject.Value<int>("count");
            var productIds = ParseCategory(jObject);
            return (totalCount, productIds);
        }

        protected async Task<int> GetCategoryPagesCount(string categoryName, CancellationToken cancellationToken)
        {
            var (totalCount, productIds) = await GetProductIdsInCategory(categoryName, 1, cancellationToken);
            if (productIds.Length == 0)
                return 0;
            return (int)Math.Ceiling(totalCount / (double)productIds.Length);
        }

    

        private enum FoodValueType
        {
            None,
            Energy,
            Volume,
            Mass
        }
    }
}