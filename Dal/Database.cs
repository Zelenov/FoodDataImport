using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Dal
{
    public interface IDal
    {
        IDbConnection GetConnection();
        Task AddOrUpdateFoodDataAsync(IDbConnection connection, FoodData foodData);
        Task InitializeAsync(IDbConnection connection);
        Task<IEnumerable<ProductId>> FindProductIds(IDbConnection connection, ShopType? shopType = null, ProductIdStatus[] statuses = null);
        Task AddOrUpdateProductIdAsync(IDbConnection connection, ProductId productId);
    }
}