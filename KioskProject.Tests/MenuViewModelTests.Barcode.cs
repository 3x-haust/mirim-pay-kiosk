namespace KioskProject.Tests;

public sealed partial class MenuViewModelTests
{
    [Fact]
    public void ScanBarcode_adds_one_known_available_item_to_shared_cart()
    {
        // Given
        var item = CreateMenu(1, "Drinks", 20);
        var cart = CreateCart();
        var menu = CreateMenuViewModel(CreateDataService(new[] { item }).Proxy, cart);
        Write(menu, "BarcodeInput", "1");

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        var cartItem = Assert.Single(ReadItems(cart, "CartItems"));
        Assert.Same(item, Read<object>(cartItem, "Menu"));
        Assert.Equal(1, Read<int>(cartItem, "Quantity"));
        Assert.Equal(2500, Read<int>(cart, "TotalPrice"));
        Assert.Equal(string.Empty, Read<string>(menu, "Status"));
        Assert.Equal(string.Empty, Read<string>(menu, "BarcodeInput"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.0")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("2147483648")]
    public void ScanBarcode_reports_malformed_input_without_cart_mutation(string barcode)
    {
        // Given
        var cart = CreateCart();
        var menu = CreateMenuViewModel(
            CreateDataService(new[] { CreateMenu(1, "Drinks", 20) }).Proxy,
            cart);
        Write(menu, "BarcodeInput", barcode);

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        Assert.Empty(ReadItems(cart, "CartItems"));
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
        Assert.Equal("invalid-barcode", Read<string>(menu, "Status"));
        Assert.Equal(barcode, Read<string>(menu, "BarcodeInput"));
    }

    [Fact]
    public void ScanBarcode_reports_unknown_id_without_cart_mutation()
    {
        // Given
        var cart = CreateCart();
        var menu = CreateMenuViewModel(
            CreateDataService(new[] { CreateMenu(1, "Drinks", 20) }).Proxy,
            cart);
        Write(menu, "BarcodeInput", "999");

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        Assert.Empty(ReadItems(cart, "CartItems"));
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
        Assert.Equal("unknown-barcode", Read<string>(menu, "Status"));
        Assert.Equal("999", Read<string>(menu, "BarcodeInput"));
    }

    [Fact]
    public void ScanBarcode_reports_out_of_stock_without_cart_mutation()
    {
        // Given
        var unavailable = CreateMenu(2, "Drinks", 0);
        var cart = CreateCart();
        var menu = CreateMenuViewModel(CreateDataService(new[] { unavailable }).Proxy, cart);
        Write(menu, "BarcodeInput", "2");

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        Assert.Empty(ReadItems(cart, "CartItems"));
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
        Assert.Equal("out-of-stock", Read<string>(menu, "Status"));
        Assert.Equal("2", Read<string>(menu, "BarcodeInput"));
    }

    [Fact]
    public void ScanBarcode_reports_out_of_stock_when_cart_already_contains_all_stock()
    {
        // Given
        var item = CreateMenu(3, "Food", 1);
        Write(item, "Price", 500);
        var cart = CreateCart();
        Invoke(cart, "AddToCart", item, 1);
        var menu = CreateMenuViewModel(CreateDataService(new[] { item }).Proxy, cart);
        Write(menu, "BarcodeInput", "3");

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        var cartItem = Assert.Single(ReadItems(cart, "CartItems"));
        Assert.Equal(1, Read<int>(cartItem, "Quantity"));
        Assert.Equal(500, Read<int>(cart, "TotalPrice"));
        Assert.Equal("out-of-stock", Read<string>(menu, "Status"));
    }

    [Fact]
    public void Successful_scan_clears_stale_status_after_a_failed_scan()
    {
        // Given
        var item = CreateMenu(1, "Drinks", 20);
        var cart = CreateCart();
        var menu = CreateMenuViewModel(CreateDataService(new[] { item }).Proxy, cart);
        Write(menu, "BarcodeInput", "abc");
        Invoke(menu, "ScanBarcode");
        Assert.Equal("invalid-barcode", Read<string>(menu, "Status"));
        Write(menu, "BarcodeInput", "1");

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        Assert.Single(ReadItems(cart, "CartItems"));
        Assert.Equal(2500, Read<int>(cart, "TotalPrice"));
        Assert.Equal(string.Empty, Read<string>(menu, "Status"));
        Assert.Equal(string.Empty, Read<string>(menu, "BarcodeInput"));
    }

    [Fact]
    public void ScanBarcode_preserves_fatal_load_status_and_cart_state()
    {
        // Given
        var cart = CreateCart();
        var menu = CreateMenuViewModel(
            CreateDataService(Array.Empty<object>(), new IOException("load failed")).Proxy,
            cart);
        Write(menu, "BarcodeInput", "1");

        // When
        Invoke(menu, "ScanBarcode");

        // Then
        Assert.Empty(ReadItems(cart, "CartItems"));
        Assert.Equal("load-error", Read<string>(menu, "Status"));
        Assert.Equal("1", Read<string>(menu, "BarcodeInput"));
    }
}
