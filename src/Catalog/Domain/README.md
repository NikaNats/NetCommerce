# Catalog Domain Layer Implementation

This document describes the complete domain layer implementation for the Catalog Service, following Domain-Driven Design (DDD) principles and object-oriented design patterns.

## Architecture Overview

The domain layer is the heart of the Catalog Service, containing all business logic, rules, and domain knowledge. It is completely isolated from external concerns (databases, APIs, messaging) and represents the core business concepts.

## Design Principles Applied

### Domain-Driven Design (DDD)
- **Rich Domain Model**: Business logic lives within domain objects
- **Ubiquitous Language**: Domain concepts match business terminology
- **Aggregate Boundaries**: Clear consistency boundaries around related entities

### Object-Oriented Design Principles
- **Encapsulation**: Private setters, state changes only through methods
- **High Cohesion**: Each class has a single, well-defined responsibility
- **Low Coupling**: Aggregates reference each other by ID, not direct objects
- **SOLID Principles**: Clean, maintainable, extensible design

### Design Patterns
- **Factory Method**: Safe object creation with valid initial state
- **Repository Pattern**: Data access abstraction at domain level
- **Domain Events**: Capture and communicate business-significant events

## Domain Components

### Base Infrastructure (`Common/`)

#### AggregateRoot<T>
Base class for aggregate roots providing:
- Domain event handling and collection
- Entity identity through strongly-typed IDs
- Event clearing after dispatch

#### Entity<T>
Base class for entities with:
- Identity-based equality comparison
- Type-safe ID handling
- Proper hash code implementation

#### ValueObject
Base class for immutable value objects with:
- Value-based equality comparison
- Immutable design
- Structural equality through components

#### IDomainEvent & DomainEvent
- Interface and base class for domain events
- Automatic timestamp and unique ID generation
- Business event representation

#### BusinessRuleException
Domain-specific exception for business rule violations.

### Value Objects (`ValueObjects/`)

#### Strongly-Typed IDs
- **ProductId, VariantId, ProductTypeId, BrandId, CategoryId**
- Type safety preventing ID mix-ups
- Implicit conversion to GUID when needed
- Factory methods for creation

#### Business Value Objects
- **Price**: Amount with currency, validation against negative values
- **Attribute**: Name-value pairs for product characteristics
- **SKU**: Stock Keeping Unit with format validation

### Enums (`Enums/`)
- **ProductStatus**: Draft, Published, Archived
- **DataType**: Text, Number, Boolean, DateTime, Decimal

### Domain Events (`Events/`)
- **ProductCreated**: When a product is created
- **ProductPublished**: When a product is made available
- **ProductArchived**: When a product is removed from catalog
- **ProductDescriptionUpdated**: When product description changes
- **VariantCreated**: When a new variant is created
- **VariantPriceChanged**: When variant price is updated
- **ProductTypeSchemaChanged**: When product type structure evolves

### Entities (`Entities/`)

#### AttributeDefinition
Internal entity within ProductType that defines:
- Attribute name, data type, and requirements
- Optional description and default values
- Modifiable constraints (required status, description)

### Aggregate Roots (`Aggregates/`)

#### Product
**Responsibilities:**
- Product lifecycle management (Draft → Published → Archived)
- Descriptive attribute management
- Brand association
- ProductType compliance and version tracking

**Key Methods:**
- `CreateDraft()`: Factory method for new products
- `Publish()`: Publishes product with business rule validation
- `Archive()`: Removes from active catalog
- `ChangeDescription()`: Updates product description
- `UpdateCompliance()`: Handles ProductType schema evolution

#### Variant
**Responsibilities:**
- Represents sellable units (SKUs) of products
- Price management with change tracking
- Defining attribute management (size, color, etc.)

**Key Methods:**
- `CreateFromSku()`: Factory method for new variants
- `UpdatePrice()`: Price changes with event generation
- `DefineAttributes()`: Sets variant distinguishing characteristics

#### ProductType
**Responsibilities:**
- Product structure and schema definition
- Attribute definition management
- Schema versioning and evolution
- Validation rule enforcement

**Key Methods:**
- `Create()`: Factory method for new product types
- `AddNewAttribute()`: Adds new attribute (increments version)
- `UpdateAttribute()`: Modifies existing attributes
- `ValidateAttributes()`: Validates product attributes against schema

#### Category
**Responsibilities:**
- Hierarchical product organization
- Navigation structure management
- Active/inactive status management

**Key Methods:**
- `Create()`: Factory method for categories
- `SetParentCategory()`: Establishes hierarchy
- `Activate()`/`Deactivate()`: Visibility management

#### Brand
**Responsibilities:**
- Brand information management
- Logo and website URL handling
- Active status management

**Key Methods:**
- `Create()`: Factory method for brands
- `SetLogoUrl()`/`SetWebsiteUrl()`: Media and web presence

### Repository Contracts (`Repositories/`)

Following the Repository pattern, domain defines interfaces for data access:
- **IProductRepository**: Product aggregate persistence
- **IVariantRepository**: Variant aggregate persistence
- **IProductTypeRepository**: ProductType aggregate persistence
- **ICategoryRepository**: Category aggregate persistence
- **IBrandRepository**: Brand aggregate persistence

Each repository provides:
- Standard CRUD operations
- Aggregate-specific query methods
- Business rule validation support (uniqueness checks, etc.)

### Domain Services (`Services/`)

#### ProductPublishingService
Handles complex business logic spanning multiple aggregates:
- Product publication eligibility checking
- Cross-aggregate business rule enforcement
- Publication process orchestration

## Business Rules Implemented

### Product Rules
1. Product name is required
2. Only draft products can be published
3. Products must have active variants to be published
4. Product must comply with ProductType schema version

### Variant Rules
1. SKU must be unique across all variants
2. SKU format validation (alphanumeric, hyphens, underscores)
3. Price cannot be negative
4. Currency must be 3-letter code

### ProductType Rules
1. ProductType name must be unique
2. Attribute names must be unique within type
3. Schema changes increment version number
4. Required attributes must be provided by products

### Category Rules
1. Category cannot be its own parent
2. Category name is required
3. Hierarchical structure support

### Brand Rules
1. Brand name must be unique
2. Website URL must be valid format
3. Brand name is required

## Usage Examples

```csharp
// Creating a new product
var productId = ProductId.New();
var productTypeId = ProductTypeId.From(existingTypeId);
var product = Product.CreateDraft(productId, "Sample T-Shirt", productTypeId, 1);

// Adding descriptive attributes
product.SetDescriptiveAttribute(Attribute.Create("Material", "100% Cotton"));
product.SetDescriptiveAttribute(Attribute.Create("Care", "Machine wash cold"));

// Publishing the product (requires domain service)
var publishingService = new ProductPublishingService(variantRepository);
var published = await publishingService.PublishProductAsync(product);

// Creating a variant
var variantId = VariantId.New();
var sku = SKU.From("TSH-RED-M");
var variant = Variant.CreateFromSku(variantId, productId, sku);

// Setting price and defining attributes
var price = Price.From(29.99m, "USD");
variant.UpdatePrice(price);
variant.SetDefiningAttribute(Attribute.Create("Color", "Red"));
variant.SetDefiningAttribute(Attribute.Create("Size", "Medium"));
```

## Benefits Achieved

1. **Type Safety**: Strongly-typed IDs prevent common mistakes
2. **Business Logic Centralization**: All rules live in domain objects
3. **Consistency**: Aggregate boundaries ensure data integrity
4. **Extensibility**: Easy to add new attributes, events, and rules
5. **Testability**: Rich domain model enables comprehensive testing
6. **Maintainability**: Clear separation of concerns and responsibilities
7. **Domain Events**: Enables event-driven architecture and integration

This implementation provides a solid foundation for the Catalog Service, encapsulating complex business logic while maintaining flexibility for future evolution.