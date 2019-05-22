using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Threading.Tasks;
using Dal;
using Dapper;
using Microsoft.Extensions.Options;

namespace SqLiteDal
{
    public class SqLiteDal : IDal
    {
        private readonly string _connectionString;

        public SqLiteDal(IOptions<ConnectionStrings> options)
        {
            _connectionString = options.Value.FoodDatabase;
        }

        public async Task InitializeAsync(IDbConnection connection)
        {

                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var settingsExists = await connection.QueryFirstOrDefaultAsync<int>(
                            @"SELECT 1 FROM sqlite_master WHERE name='settings'", transaction: transaction) == 1;
                        var version = 0;
                        if (settingsExists)
                            version = int.Parse(await connection.QueryFirstOrDefaultAsync<string>(
                                @"SELECT value FROM settings WHERE key='version'", transaction: transaction));
                        if (version == 0)
                            await connection.ExecuteAsync(@"
CREATE TABLE settings (
  key TEXT NOT NULL, 
  value TEXT, 
  PRIMARY KEY (key)
);

CREATE TABLE shopType (
  id INTEGER NOT NULL,  
  name TEXT, 
  PRIMARY KEY (id)
);


CREATE TABLE productIdStatus (
  id INTEGER NOT NULL,  
  name TEXT, 
  PRIMARY KEY (id)
);

CREATE TABLE productId (
  shopTypeId SMALLINT NOT NULL, 
  externalId INTEGER NOT NULL, 
  statusId SMALLINT NOT NULL, 
  created TEXT NOT NULL, 
  updated TEXT NOT NULL, 
  PRIMARY KEY (shopTypeId, externalId),
  FOREIGN KEY(shopTypeId) REFERENCES shopType(id)
  FOREIGN KEY(statusId) REFERENCES productIdStatus(id)
);

CREATE TABLE food (
  shopTypeId SMALLINT NOT NULL, 
  externalId INTEGER NOT NULL, 
  category TEXT, 
  name TEXT, 
  url TEXT, 
  weight DECIMAL, 
  volume DECIMAL, 
  price DECIMAL NOT NULL, 
  energy DECIMAL, 
  proteins DECIMAL, 
  fats DECIMAL, 
  carbohydrates DECIMAL, 
  created TEXT NOT NULL, 
  updated TEXT NOT NULL, 
  PRIMARY KEY (shopTypeId, externalId),
  FOREIGN KEY(shopTypeId) REFERENCES shopType(id)
);

INSERT INTO shopType (id, name)
VALUES (1, 'Перекресток');

INSERT INTO productIdStatus (id, name)
VALUES
(1, 'Новый'),
(2, 'Импортирован'),
(3, 'Ошибка'),
(4, 'Пропущен');

INSERT INTO settings (key, value)
VALUES ('version', '1')


", transaction: transaction);
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            
        }

        public async Task<IEnumerable<ProductId>> FindProductIds(IDbConnection connection, ShopType? shopType = null,
            ProductIdStatus[] statuses = null)
        {

                return await connection.QueryAsync<ProductId>($@"SELECT
shopTypeId as {nameof(ProductId.ShopType)},
externalId as {nameof(ProductId.ExternalId)},
statusId as {nameof(ProductId.Status)},
created as {nameof(ProductId.Created)},
updated as {nameof(ProductId.Updated)}
FROM productId
WHERE
    (@ShopType IS NULL OR productId.shopTypeId = @ShopType)
AND (@StatusesPresent = 0 OR productId.statusId IN @Statuses)
ORDER BY ExternalId", new {ShopType = shopType, StatusesPresent = statuses != null, Statuses = statuses});
            
        }


        public async Task AddOrUpdateProductIdAsync(IDbConnection connection, ProductId productId)
        {

                await connection.QueryFirstOrDefaultAsync(
                    $@"INSERT INTO productId (shopTypeId, externalId, statusId, created, updated)
  VALUES(@{nameof(ProductId.ShopType)},
@{nameof(ProductId.ExternalId)},
@{nameof(ProductId.Status)},
@{nameof(ProductId.Created)},
@{nameof(ProductId.Updated)}
) 
  ON CONFLICT(shopTypeId, externalId) 
  DO UPDATE SET 
updated = @{nameof(ProductId.Updated)},
statusId = @{nameof(ProductId.Status)}
", productId);
            
        }

        public async Task AddOrUpdateFoodDataAsync(IDbConnection connection, FoodData foodData)
        {

                await connection.QueryFirstOrDefaultAsync(
                    $@"INSERT INTO food (shopTypeId, externalId, category, name, url, weight, volume, price, energy, proteins, fats, carbohydrates, created, updated)
  VALUES(@{nameof(FoodData.ShopType)},
@{nameof(FoodData.ExternalId)},
@{nameof(FoodData.Category)},
@{nameof(FoodData.Name)},
@{nameof(FoodData.Url)},
@{nameof(FoodData.Weight)},
@{nameof(FoodData.Volume)},
@{nameof(FoodData.Price)},
@{nameof(FoodData.Energy)},
@{nameof(FoodData.Proteins)},
@{nameof(FoodData.Fats)},
@{nameof(FoodData.Carbohydrates)},
@{nameof(FoodData.Created)},
@{nameof(FoodData.Updated)}
) 
  ON CONFLICT(shopTypeId, externalId) 
  DO UPDATE SET 
category = @{nameof(FoodData.Category)},
name = @{nameof(FoodData.Name)},
url = @{nameof(FoodData.Url)},
weight = @{nameof(FoodData.Weight)},
volume = @{nameof(FoodData.Volume)},
price = @{nameof(FoodData.Price)},
energy = @{nameof(FoodData.Energy)},
proteins = @{nameof(FoodData.Proteins)},
fats = @{nameof(FoodData.Fats)},
carbohydrates = @{nameof(FoodData.Carbohydrates)},
updated = @{nameof(FoodData.Updated)}", foodData);
            
        }

        public IDbConnection GetConnection()
        {
            return new SQLiteConnection(_connectionString);
        }
    }
}