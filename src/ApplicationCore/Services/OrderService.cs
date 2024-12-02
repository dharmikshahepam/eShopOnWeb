using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);

        //await SaveOrderRequestToBlobAsync(order);
        await SaveOrderInCosmosAsync(order);
    }

    public async Task SaveOrderRequestToBlobAsync(Order order)
    {
        var functionUrl = _configuration["OrderItemsReserverFunctionUrl"];
        //functionUrl = "http://localhost:7246/api/OrderItemReserver";
        if (!string.IsNullOrWhiteSpace(functionUrl))
        {
            var orderDetails = new
            {
                ItemId = order.Id.ToString(),
                Quantity = order.OrderItems.Sum(x => x.Units),
            };

            var jsonContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(orderDetails), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(functionUrl, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Failed to notify warehouse");
            }
        }

    }

    public async Task SaveOrderInCosmosAsync(Order order)
    {
        var functionUrl = _configuration["OrderReserverFunctionUrl"];
        functionUrl = "http://localhost:7246/api/OrderReserver";
        if (!string.IsNullOrWhiteSpace(functionUrl))
        {
            var orderDetails = new
            {
                ShippingAddress = $"{order.ShipToAddress.Street}, {order.ShipToAddress.City},  {order.ShipToAddress.State} - {order.ShipToAddress.ZipCode}, {order.ShipToAddress.ZipCode}",
                Items = order.OrderItems,
                OrderDate = order.OrderDate,
                OrderId = order.Id
            };

            var jsonContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(orderDetails), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(functionUrl, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Failed to save Order" + response.StatusCode + response);
            }
        }

    }
}
