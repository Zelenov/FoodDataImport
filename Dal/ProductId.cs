using System;

namespace Dal
{
    public class ProductId
    {
        public ShopType ShopType { get; set; }
        public int ExternalId { get; set; }
        public ProductIdStatus Status { get; set; }
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
    }
}