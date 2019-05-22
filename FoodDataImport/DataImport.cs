using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dal;
using Microsoft.Extensions.Logging;
using Shop;

namespace FoodDataImport
{
    public class DataImport
    {
        private readonly IDal _dal;
        private readonly ILogger _logger;
        private readonly IShop[] _shops;

        public DataImport(IShop[] shops, IDal dal, ILogger<DataImport> logger)
        {
            _shops = shops;
            _dal = dal;
            _logger = logger;
        }

        private async Task AddProductId(IDbConnection connection, ProductId productId)
        {
            await _dal.AddOrUpdateProductIdAsync(connection, productId);
            _logger.LogDebug($"Есть {productId.ExternalId} в магазине {productId.ShopType}");
        }

        public async Task ImportProductIdsAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            using (var connection = _dal.GetConnection())
            {
                connection.Open();
                // ReSharper disable once AccessToDisposedClosure
                await Task.WhenAll(_shops.Select(async shop => await shop.GetProductIds(productId => AddProductId(connection, productId),
                    new Progress<double>(p => progress?.Report(p / _shops.Length)), cancellationToken)));
            }
        }

        public async Task ImportProductsDataAsync(IProgress<double> progress, bool onlyNewAndError,
            CancellationToken cancellationToken)
        {
            using (var connection = _dal.GetConnection())
            {
                connection.Open();
                var productIds =
                    (await _dal.FindProductIds(connection, statuses: onlyNewAndError
                        ? new[] {ProductIdStatus.New, ProductIdStatus.Error}
                        : null)).ToArray();
                var currentProgress = 0;
                await Task.WhenAll(_shops.Select(async shop =>
                {
                    await Task.WhenAll(productIds.Where(p => p.ShopType == shop.ShopType)
                       .Select(async p =>
                        {
                            try
                            {
                                var product = await shop.Import(p.ExternalId, cancellationToken);
                                if (product != null)
                                    await ImportData(connection, product, p);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                await ErrorData(connection, p, ex);
                            }
                            finally
                            {
                                var pr = Interlocked.Increment(ref currentProgress);
                                progress?.Report(pr / (double) productIds.Length);
                            }
                        }));
                }));
            }
        }

        private async Task ErrorData(IDbConnection connection, ProductId productId, Exception exception)
        {
            _logger.LogError($"Проблема с продуктом {productId.ExternalId} в магазине {productId.ShopType}", exception);
            productId.Status = ProductIdStatus.Error;
            productId.Updated = DateTime.UtcNow;
            await _dal.AddOrUpdateProductIdAsync(connection, productId);
        }

        private async Task ImportData(IDbConnection connection, FoodData food, ProductId productId)
        {
            if (food != null)
            {
                await _dal.AddOrUpdateFoodDataAsync(connection, food);
                _logger.LogDebug($"Добавлен {food.Name}");
            }

            productId.Status = food == null ? ProductIdStatus.Skipped : ProductIdStatus.Imported;
            productId.Updated = DateTime.UtcNow;
            await _dal.AddOrUpdateProductIdAsync(connection, productId);
        }
    }
}