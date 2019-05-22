using System;
using System.Threading;
using System.Threading.Tasks;
using Dal;

namespace Shop
{
    public interface IShop
    {
        ShopType ShopType { get; }

        Task GetProductIds(Func<ProductId, Task> dataAction, IProgress<double> progress,
            CancellationToken cancellationToken);

        Task<FoodData> Import(int productId, CancellationToken cancellationToken);
    }
}