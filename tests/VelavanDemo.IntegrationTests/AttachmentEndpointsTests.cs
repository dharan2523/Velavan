using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Authentication.Models;
using VelavanDemo.Application.Common;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.IntegrationTests;

public sealed class AttachmentEndpointsTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nS8AAAAASUVORK5CYII=");

    private static readonly byte[] SmallPdf = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF");

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AttachmentEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _factory.EnsureStarted();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task ProductImageLifecycle_UploadsListsPrimaryOrdersAndDeletes()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var firstUpload = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "First Image.png",
            "image/png");

        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        var firstImage = await firstUpload.Content.ReadFromJsonAsync<ProductImageResponse>(JsonOptions);

        Assert.NotNull(firstImage);
        Assert.True(firstImage!.IsPrimary);
        Assert.Equal(0, firstImage.SortOrder);

        var secondUpload = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "second.png",
            "image/png");

        Assert.Equal(HttpStatusCode.Created, secondUpload.StatusCode);

        var secondImage = await secondUpload.Content.ReadFromJsonAsync<ProductImageResponse>(JsonOptions);

        Assert.NotNull(secondImage);
        Assert.False(secondImage!.IsPrimary);
        Assert.Equal(1, secondImage.SortOrder);

        var listResponse = await client.GetAsync($"/api/admin/products/{productId}/images");
        var images = await listResponse.Content.ReadFromJsonAsync<List<ProductImageResponse>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(images);
        Assert.Equal(2, images!.Count);
        var firstId = firstImage.Id;
        var secondId = secondImage.Id;
        Assert.Equal(firstId, images[0].Id);

        var storageKey = await GetStorageKeyAsync(firstId);
        var storedPath = GetStoragePath(storageKey);

        Assert.True(File.Exists(storedPath));

        var primaryResponse = await client.PatchAsync(
            $"/api/admin/products/{productId}/images/{secondId}/primary",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, primaryResponse.StatusCode);

        var afterPrimary = await GetImagesAsync(client, productId);

        Assert.True(afterPrimary.Single(image => image.Id == secondId).IsPrimary);
        Assert.False(afterPrimary.Single(image => image.Id == firstId).IsPrimary);

        var reorderResponse = await client.PutAsJsonAsync(
            $"/api/admin/products/{productId}/images/order",
            new ReorderProductImagesRequest { AttachmentIds = new List<int> { secondId, firstId } },
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);

        var afterReorder = await GetImagesAsync(client, productId);

        Assert.Equal(0, afterReorder.Single(image => image.Id == secondId).SortOrder);
        Assert.Equal(1, afterReorder.Single(image => image.Id == firstId).SortOrder);

        var deleteResponse = await client.DeleteAsync(
            $"/api/admin/products/{productId}/images/{firstId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(storedPath));
        Assert.Single(await GetImagesAsync(client, productId));
    }

    [Fact]
    public async Task ProductImage_WithWrongOwner_ReturnsNotFound()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();
        var otherProductId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "image.png",
            "image/png");

        var image = await upload.Content.ReadFromJsonAsync<ProductImageResponse>(JsonOptions);

        Assert.NotNull(image);

        var response = await client.DeleteAsync(
            $"/api/admin/products/{otherProductId}/images/{image!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CustomerDocument_IsServedAsDownloadAndCanBeDeleted()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var customerId = await _factory.CreateCustomerAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/customers/{customerId}/documents",
            SmallPdf,
            "invoice.pdf",
            "application/pdf");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var document = await upload.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        Assert.NotNull(document);
        Assert.Equal("invoice.pdf", document!.OriginalFileName);
        Assert.Equal("application/pdf", document.ContentType);

        var metadataResponse = await client.GetAsync($"/api/attachments/{document.Id}");
        var metadataJson = await metadataResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        Assert.DoesNotContain("storageKey", metadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.AttachmentRootPath, metadataJson, StringComparison.OrdinalIgnoreCase);

        var contentResponse = await client.GetAsync($"/api/attachments/{document.Id}/content");

        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("attachment", contentResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", contentResponse.Headers.GetValues("X-Content-Type-Options").Single());

        var cacheControl = contentResponse.Headers.CacheControl;

        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.Private);
        Assert.True(cacheControl.NoStore);
        Assert.NotNull(contentResponse.Headers.ETag);
        Assert.Equal(SmallPdf, await contentResponse.Content.ReadAsByteArrayAsync());

        var storageKey = await GetStorageKeyAsync(document.Id);
        var storedPath = GetStoragePath(storageKey);

        Assert.True(File.Exists(storedPath));

        var deleteResponse = await client.DeleteAsync(
            $"/api/admin/customers/{customerId}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(storedPath));

        var missingResponse = await client.GetAsync($"/api/attachments/{document.Id}/content");

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task OrderImage_IsServedInlineByDefault()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var orderId = await _factory.CreateOrderAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/orders/{orderId}/attachments",
            OnePixelPng,
            "proof.png",
            "image/png");

        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        Assert.NotNull(attachment);

        var contentResponse = await client.GetAsync($"/api/attachments/{attachment!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("inline", contentResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("image/png", contentResponse.Content.Headers.ContentType?.MediaType);

        var downloadResponse = await client.GetAsync(
            $"/api/attachments/{attachment.Id}/content?download=true");

        Assert.Equal("attachment", downloadResponse.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Upload_WithMismatchedContentType_ReturnsUnsupportedMediaType()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "image.png",
            "image/jpeg");

        Assert.True(
            response.StatusCode == HttpStatusCode.UnsupportedMediaType,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_WhenProductHasMaximumImages_ReturnsConflict()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        for (var index = 0; index < AttachmentPolicies.ProductImageMaxCount; index++)
        {
            var upload = await UploadAsync(
                client,
                $"/api/admin/products/{productId}/images",
                OnePixelPng,
                $"image-{index}.png",
                "image/png");

            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "too-many.png",
            "image/png");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedAndStandardUsers_CannotAccessAttachments()
    {
        var productId = await _factory.CreateProductAsync();
        var standardUser = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(standardUser.Username, standardUser.Password);

        using var standardClient = CreateAuthenticatedClient(token);

        var unauthenticatedResponse = await _client.GetAsync(
            $"/api/admin/products/{productId}/images");
        var forbiddenResponse = await standardClient.GetAsync(
            $"/api/admin/products/{productId}/images");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_WithUnsupportedExtensionOrSvg_ReturnsUnsupportedMediaType()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var executableResponse = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            SmallPdf,
            "payload.exe",
            "application/octet-stream");

        var svgResponse = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"),
            "image.svg",
            "image/svg+xml");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, executableResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, svgResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_WithSignatureThatDoesNotMatchExtension_ReturnsUnsupportedMediaType()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            SmallPdf,
            "not-an-image.png",
            "image/png");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

        var images = await GetImagesAsync(client, productId);
        Assert.Empty(images);
    }

    [Fact]
    public async Task Upload_WhenProductImageExceedsSizeLimit_ReturnsPayloadTooLarge()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var oversizedContent = new byte[AttachmentPolicies.ProductImageMaxBytes + 1];

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            oversizedContent,
            "oversized.png",
            "image/png");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WhenCustomerHasMaximumDocuments_ReturnsConflict()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var customerId = await _factory.CreateCustomerAsync();

        using var client = CreateAuthenticatedClient(token);

        for (var index = 0; index < AttachmentPolicies.DocumentMaxCount; index++)
        {
            var upload = await UploadAsync(
                client,
                $"/api/admin/customers/{customerId}/documents",
                SmallPdf,
                $"document-{index}.pdf",
                "application/pdf");

            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var response = await UploadAsync(
            client,
            $"/api/admin/customers/{customerId}/documents",
            SmallPdf,
            "too-many.pdf",
            "application/pdf");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WhenOrderHasMaximumAttachments_ReturnsConflict()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var orderId = await _factory.CreateOrderAsync();

        using var client = CreateAuthenticatedClient(token);

        for (var index = 0; index < AttachmentPolicies.DocumentMaxCount; index++)
        {
            var upload = await UploadAsync(
                client,
                $"/api/admin/orders/{orderId}/attachments",
                SmallPdf,
                $"order-{index}.pdf",
                "application/pdf");

            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var response = await UploadAsync(
            client,
            $"/api/admin/orders/{orderId}/attachments",
            SmallPdf,
            "too-many.pdf",
            "application/pdf");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CustomerDocument_WithWrongOwner_ReturnsNotFoundAndKeepsDocument()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var customerId = await _factory.CreateCustomerAsync();
        var otherCustomerId = await _factory.CreateCustomerAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/customers/{customerId}/documents",
            SmallPdf,
            "invoice.pdf",
            "application/pdf");

        var document = await upload.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        Assert.NotNull(document);

        var response = await client.DeleteAsync(
            $"/api/admin/customers/{otherCustomerId}/documents/{document!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var documentsResponse = await client.GetAsync(
            $"/api/admin/customers/{customerId}/documents");
        var documents = await documentsResponse.Content
            .ReadFromJsonAsync<List<AttachmentResponse>>(JsonOptions);

        Assert.Single(documents!);
    }

    [Fact]
    public async Task OrderAttachment_WithWrongOwner_ReturnsNotFoundAndKeepsAttachment()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var orderId = await _factory.CreateOrderAsync();
        var otherOrderId = await _factory.CreateOrderAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/orders/{orderId}/attachments",
            SmallPdf,
            "proof.pdf",
            "application/pdf");

        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        Assert.NotNull(attachment);

        var response = await client.DeleteAsync(
            $"/api/admin/orders/{otherOrderId}/attachments/{attachment!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var attachmentsResponse = await client.GetAsync(
            $"/api/admin/orders/{orderId}/attachments");
        var attachments = await attachmentsResponse.Content
            .ReadFromJsonAsync<List<AttachmentResponse>>(JsonOptions);

        Assert.Single(attachments!);
    }

    [Fact]
    public async Task MissingOwner_ReturnsNotFoundForListingAndUpload()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);

        var listResponse = await client.GetAsync("/api/admin/products/999999/images");
        var uploadResponse = await UploadAsync(
            client,
            "/api/admin/products/999999/images",
            OnePixelPng,
            "image.png",
            "image/png");

        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, uploadResponse.StatusCode);
    }

    [Fact]
    public async Task Reorder_WithDuplicateOrUnknownImages_ReturnsBadRequest()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        var firstUpload = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "first.png",
            "image/png");
        var firstImage = await firstUpload.Content.ReadFromJsonAsync<ProductImageResponse>(JsonOptions);

        var secondUpload = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "second.png",
            "image/png");
        var secondImage = await secondUpload.Content.ReadFromJsonAsync<ProductImageResponse>(JsonOptions);

        var duplicateResponse = await client.PutAsJsonAsync(
            $"/api/admin/products/{productId}/images/order",
            new ReorderProductImagesRequest
            {
                AttachmentIds = new List<int> { firstImage!.Id, firstImage.Id }
            },
            JsonOptions);

        var unknownResponse = await client.PutAsJsonAsync(
            $"/api/admin/products/{productId}/images/order",
            new ReorderProductImagesRequest
            {
                AttachmentIds = new List<int> { firstImage.Id, secondImage!.Id + 1000 }
            },
            JsonOptions);

        var nullResponse = await client.PutAsync(
            $"/api/admin/products/{productId}/images/order",
            new StringContent("null", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nullResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_WhenScannerRejectsFile_ReturnsUnprocessableEntity()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUploadedFileScanner>();
                services.AddSingleton<IUploadedFileScanner>(
                    new RejectingUploadedFileScanner(OperationError.MalwareDetected));
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "unsafe.png",
            "image/png");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await GetImagesAsync(client, productId));
    }

    [Fact]
    public async Task Upload_WhenScannerIsUnavailable_ReturnsServiceUnavailable()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUploadedFileScanner>();
                services.AddSingleton<IUploadedFileScanner>(
                    new RejectingUploadedFileScanner(OperationError.ScanUnavailable));
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "unscanned.png",
            "image/png");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WhenStorageQuotaIsExceeded_ReturnsInsufficientStorage()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Attachments:Storage:MaxTotalBytes"] = "1"
                });
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "quota.png",
            "image/png");

        Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentUploads_RespectProductImageLimit()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var client = CreateAuthenticatedClient(token);

        for (var index = 0; index < 7; index++)
        {
            var response = await UploadAsync(
                client,
                $"/api/admin/products/{productId}/images",
                OnePixelPng,
                $"image-{index}.png",
                "image/png");

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var firstUpload = UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "concurrent-a.png",
            "image/png");
        var secondUpload = UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "concurrent-b.png",
            "image/png");

        var responses = await Task.WhenAll(firstUpload, secondUpload);
        var statuses = responses.Select(response => response.StatusCode).ToArray();

        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);

        var images = await GetImagesAsync(client, productId);

        Assert.Equal(8, images.Count);
        Assert.Single(images, image => image.IsPrimary);
    }

    [Fact]
    public void ProductionStartup_WithoutRealScanner_Fails()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Attachments:Scanner:Provider"] = AttachmentScannerOptions.NoOpProvider
                });
            });
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "malware scanner",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionStartup_RejectsHttpScannerEndpoint()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Attachments:Scanner:Provider"] = AttachmentScannerOptions.HttpProvider,
                    ["Attachments:Scanner:Endpoint"] = "http://localhost:5099/scan",
                    ["Attachments:Scanner:ApiKey"] = "production-scanner-secret"
                });
            });
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "HTTPS",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionStartup_RejectsMissingScannerCredentials()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Attachments:Scanner:Provider", AttachmentScannerOptions.HttpProvider);
            builder.UseSetting("Attachments:Scanner:Endpoint", "https://localhost:5099/scan");
            builder.UseSetting("Attachments:Scanner:ApiKey", "");
            builder.UseSetting("Attachments:Scanner:TimeoutSeconds", "30");
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "ApiKey",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionStartup_AcceptsValidHttpsScannerConfiguration()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Attachments:Scanner:Provider", AttachmentScannerOptions.HttpProvider);
            builder.UseSetting("Attachments:Scanner:Endpoint", "https://localhost:5099/scan");
            builder.UseSetting("Attachments:Scanner:ApiKey", "production-scanner-secret");
            builder.UseSetting("Attachments:Scanner:TimeoutSeconds", "30");
            builder.UseSetting("Attachments:Storage:Provider", "Local");
            builder.UseSetting("Attachments:Storage:RootPath", _factory.AttachmentRootPath);
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task GetAttachmentContent_WhenPhysicalFileIsMissing_ReturnsServerError()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var customerId = await _factory.CreateCustomerAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/customers/{customerId}/documents",
            SmallPdf,
            "missing.pdf",
            "application/pdf");

        var document = await upload.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        Assert.NotNull(document);

        var storageKey = await GetStorageKeyAsync(document!.Id);
        var storedPath = GetStoragePath(storageKey);

        File.Delete(storedPath);

        var response = await client.GetAsync($"/api/attachments/{document.Id}/content");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(
            _factory.AttachmentRootPath,
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(storageKey, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAttachmentContent_WithMatchingETag_ReturnsNotModified()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var customerId = await _factory.CreateCustomerAsync();

        using var client = CreateAuthenticatedClient(token);

        var upload = await UploadAsync(
            client,
            $"/api/admin/customers/{customerId}/documents",
            SmallPdf,
            "cached.pdf",
            "application/pdf");

        var document = await upload.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        Assert.NotNull(document);

        var firstResponse = await client.GetAsync($"/api/attachments/{document!.Id}/content");
        var etag = firstResponse.Headers.ETag;

        Assert.NotNull(etag);

        using var cachedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/attachments/{document.Id}/content");
        cachedRequest.Headers.IfNoneMatch.Add(etag!);

        var cachedResponse = await client.SendAsync(cachedRequest);

        Assert.Equal(HttpStatusCode.NotModified, cachedResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_WhenStorageWriteFails_ReturnsServerError()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var productId = await _factory.CreateProductAsync();

        using var failingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage, FailingFileStorage>();
            });
        });

        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await UploadAsync(
            client,
            $"/api/admin/products/{productId}/images",
            OnePixelPng,
            "image.png",
            "image/png");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> LoginAndGetTokenAsync(string username, string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = username, Password = password },
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.NotNull(auth);
        return auth!.AccessToken;
    }

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string url,
        byte[] bytes,
        string fileName,
        string contentType)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);

        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(fileContent, "file", fileName);

        return client.PostAsync(url, multipart);
    }

    private static async Task<List<ProductImageResponse>> GetImagesAsync(
        HttpClient client,
        int productId)
    {
        var response = await client.GetAsync($"/api/admin/products/{productId}/images");
        response.EnsureSuccessStatusCode();

        var images = await response.Content.ReadFromJsonAsync<List<ProductImageResponse>>(JsonOptions);

        Assert.NotNull(images);
        return images!;
    }

    private async Task<string> GetStorageKeyAsync(int attachmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Attachments
            .AsNoTracking()
            .Where(attachment => attachment.Id == attachmentId)
            .Select(attachment => attachment.StorageKey)
            .SingleAsync();
    }

    private string GetStoragePath(string storageKey)
    {
        return Path.Combine(
            _factory.AttachmentRootPath,
            storageKey.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class RejectingUploadedFileScanner : IUploadedFileScanner
    {
        private readonly OperationError _error;

        public RejectingUploadedFileScanner(OperationError error)
        {
            _error = error;
        }

        public Task<OperationResult> ScanAsync(
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Failure(
                _error,
                _error == OperationError.MalwareDetected
                    ? "The uploaded file was rejected by the malware scanner."
                    : "The malware scanner is unavailable."));
        }
    }

    private sealed class FailingFileStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(
            Stream content,
            string fileExtension,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<StoredFile>(
                new IOException("The attachment storage is unavailable."));
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<Stream>(
                new FileNotFoundException("The attachment content is missing."));
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
