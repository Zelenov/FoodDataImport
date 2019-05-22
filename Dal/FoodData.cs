using System;

namespace Dal
{
    public class FoodData
    {
        public ShopType ShopType { get; set; }
        public int ExternalId { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Volume { get; set; }
        public decimal Price { get; set; }
        public decimal? Energy { get; set; }
        public decimal? Proteins { get; set; }
        public decimal? Fats { get; set; }
        public decimal? Carbohydrates { get; set; }
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
    }
}