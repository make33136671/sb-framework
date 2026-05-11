# Fork Guide (Downstream Project)

sb-api is an upstream framework. Downstream projects fork it and add business code. This guide explains how to set up and maintain a fork.

---

## Overview

```
sb-api (upstream) ──fork──> your-project (downstream)
     │                           │
     │  periodic sync            │  business code
     │  git merge upstream/main  │  in your namespace
     └──────────────────────────→│
```

**Key principle:** Keep `SB.*` namespace untouched. Write business code in your own namespace (e.g., `Wuxing.*`). Customize via inheritance + DI override, not direct modification.

---

## Step 1: Fork Repositories

### Backend (sb-api)

```bash
# Clone upstream
git clone git@your-server:sb/sb-api.git your-api
cd your-api

# Set upstream remote for future sync
git remote rename origin upstream
git remote add origin git@your-server:your-team/your-api.git
git push -u origin main
```

### Frontend (sb-admin)

```bash
git clone git@your-server:sb/sb-admin.git your-admin
cd your-admin
git remote rename origin upstream
git remote add origin git@your-server:your-team/your-admin.git
git push -u origin main
```

---

## Step 2: Backend Project Structure

Add your business project alongside framework projects:

```
your-api/
├── SB.API/              # Framework (don't modify)
├── SB.Services/         # Framework (don't modify)
├── SB.Models/           # Framework (don't modify)
├── SB.Core/             # Framework (don't modify)
├── YourProject.Services/  # Your business logic
├── YourProject.Models/    # Your entities & DTOs
└── SB.sln               # Add your projects to solution
```

```bash
# Create your projects
dotnet new classlib -n YourProject.Models
dotnet new classlib -n YourProject.Services
dotnet sln add YourProject.Models YourProject.Services

# Add references
cd YourProject.Models
dotnet add reference ../SB.Models/SB.Models.csproj

cd ../YourProject.Services
dotnet add reference ../SB.Services/SB.Services.csproj
dotnet add reference ../YourProject.Models/YourProject.Models.csproj
```

---

## Step 3: Configuration

In `appsettings.json`, set your project assembly name:

```json
{
  "ProjectAssemblyName": "YourProject.Services"
}
```

This tells the framework to scan your assembly for services and module initializers.

---

## Step 4: Adding Business Entities

```csharp
// YourProject.Models/Product.cs
using FreeSql.DataAnnotations;
using SB.Models;

namespace YourProject.Models;

[Table(Name = "YP_Product")]  // Your prefix, not SB_
public class Product : IEntity, ISoftDelete
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(StringLength = 200)]
    public string Name { get; set; }

    public decimal Price { get; set; }

    [Column(DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime CreatedTime { get; set; }

    public bool IsDeleted { get; set; }
}
```

---

## Step 5: Adding Business Services

```csharp
// YourProject.Services/ProductService.cs
using SB.Core.Attribute;
using SB.Services;
using YourProject.Models;

namespace YourProject.Services;

[AutoDependencyInjection]
public class ProductService : BaseCrudService<Product>
{
    public ProductService(IFreeSql fsql, ILogger<ProductService> logger)
        : base(fsql, logger) { }

    // Your business methods
    public virtual Result GetByCategory(int categoryId, int page = 1, int pageSize = 20)
    {
        var query = Orm.Select<Product>().Where(p => p.CategoryId == categoryId);
        return PagedResult(query, page, pageSize);
    }
}
```

---

## Step 6: Adding Controllers

```csharp
// SB.API/Controllers/ProductController.cs (add to SB.API project)
using YourProject.Services;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProductController : Controller
{
    private readonly ProductService _service;
    public ProductController(ProductService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(string field, string order, int page = 1, int pageSize = 20)
        => Ok(await _service.GetPageAsync(page, pageSize, field, order));
}
```

> **Note:** Controllers must live in `SB.API` project (or be in an assembly referenced by it) to be discovered by ASP.NET Core.

---

## Step 7: Overriding Framework Services

Override framework behavior via inheritance:

```csharp
// YourProject.Services/CustomAdminService.cs
using SB.Services;

namespace YourProject.Services;

[AutoDependencyInjection]
public class CustomAdminService : AdminService
{
    public CustomAdminService(/* same constructor params as AdminService */)
        : base(/* pass through */) { }

    // Override any virtual method
    public override Result GetList(string username = "", ...)
    {
        // Custom logic
        return base.GetList(username, ...);
    }
}
```

The DI system automatically registers `CustomAdminService` as both itself and `AdminService`, so framework controllers that inject `AdminService` will get your custom version.

---

## Step 8: Excluding Framework Controllers

If you need to replace a framework controller entirely, use `ExcludeControllers` in `appsettings.json`:

```json
{
  "ExcludeControllers": ["Dashboard", "Member"]
}
```

Then create your own controllers with the same or different routes.

---

## Step 9: Module Initializer (Seed Data)

Implement `IModuleInitializer` to register entities and seed data:

```csharp
// YourProject.Services/YourModuleInitializer.cs
using SB.Models;

namespace YourProject.Services;

[AutoDependencyInjection]
public class YourModuleInitializer : IModuleInitializer
{
    public string ModuleName => "YourProject";
    public int InitOrder => 100;  // After framework modules (0-50)

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Product),
        typeof(Order),
    };

    public List<MenuDefinition> GetMenus() => new()
    {
        new() { Name = "ProductList", Path = "/product", Title = "Product Management", Icon = "lucide:package", OrderNo = 100 },
    };

    public async Task SeedData(IFreeSql fsql)
    {
        // Seed initial data
    }
}
```

---

## Step 10: Syncing Upstream Changes

```bash
# Fetch latest upstream
git fetch upstream

# Merge upstream into your branch
git merge upstream/main

# Resolve conflicts (should be minimal if you didn't modify SB.* files)
# Conflicts typically only occur in:
#   - appsettings.json (your custom config)
#   - SB.sln (your added projects)
#   - SB.API/Controllers/ (if you added controllers here)
```

**Best practices for minimal conflicts:**
1. Never modify files in `SB.Services/`, `SB.Models/`, `SB.Core/`
2. In `SB.API/`, only add new controllers; don't modify existing ones
3. Keep `appsettings.json` changes minimal; use environment-specific overrides
4. Use `ExcludeControllers` instead of deleting framework controllers

---

## Frontend Fork

### Adding Pages

Add your pages under `sb-admin/apps/web-antd/src/views/`:

```
src/views/
├── product/          # Your new module
│   ├── List.vue
│   ├── Form.vue
│   └── api.ts
├── order/            # Your new module
│   ├── List.vue
│   └── api.ts
└── ... (framework pages)
```

### Adding Routes

Routes are menu-driven. After adding menus via `IModuleInitializer.GetMenus()` and assigning them to roles, the frontend auto-generates routes from the backend menu API.

### API Layer

```typescript
// src/views/product/api.ts
import { api } from '#/api/request';

export async function getProductList(params: any) {
  return api.get('/Product/List', { params });
}

export async function postProductAdd(data: any) {
  return api.post('/Product/Add', data);
}
```

---

## Layered Coexistence (Advanced)

For the full technical specification of the layered coexistence pattern (DI override mechanics, multi-assembly scanning, module initializer deduplication), see:

`/sb-api-layered-coexistence-plan.md`

This plan details the framework-side changes needed to fully support downstream projects without namespace conflicts.
