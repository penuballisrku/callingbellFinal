# Calling Bell — engineering guide

Read this part first. It describes the code as it is. The product brief further down ("Calling Bell Platform") is the original
requirements document; where the two differ (e.g. it mentions Dapper, Elasticsearch and a `/business` portal), the code and this guide win.

## Stack as built

- **Database:** SQL Server (local: `.\SQLEXPRESS`, database `CallingBell`). Schema and seed data come from the numbered scripts in
  `database/scripts`, run through `RunAll.sql`. There are no EF migrations in use.
- **API:** .NET 8, Clean Architecture in `backend/src`:
  - `CallingBell.Domain`: entities and constants (`DomainConstants.cs`).
  - `CallingBell.Application`: one file per feature under `Features/*`, holding the request records, FluentValidation validators and
    MediatR handlers together. Shared helpers live in `Common` (`Phones`, `IndianTime`, `ReverseGeocoder`, `ReferenceDataCache`, `GeoMath`).
  - `CallingBell.Infrastructure`: EF Core `ApplicationDbContext`, Identity/JWT, OTP, AI (Ollama), geo (MaxMind, ip-api, Overpass,
    Wikidata), Google Places, Razorpay, and notification providers.
  - `CallingBell.Api`: thin controllers (`Controllers/*.cs`), SignalR hubs, the SEO middleware that serves the built SPA.
- **Data access:** EF Core with `IUnitOfWork` / `Repository<T>()` (`Query()` tracked, `QueryNoTracking()` read-only). Not Dapper, no stored
  procedures.
- **Web:** one Vite app in `web` for all three portals: React 19 + TypeScript (strict), React Router 7, MUI 7 + Tailwind 4, TanStack
  Query 5, Zustand, React Hook Form + Zod, SignalR, AG Grid / Recharts (admin), Firebase (web push only, lazy-loaded).
- **Portals:** customer site at `/`, business owner portal at `/owner/*`, admin console at `/admin/*`. `/business/:slug` is the
  public business page (old `/business/{section}` owner links redirect to `/owner`).

## Running locally

```powershell
# Database (SQLCMD mode). -C trusts the local certificate; -I is required (QUOTED_IDENTIFIER) for the scripts.
cd database\scripts; sqlcmd -S .\SQLEXPRESS -E -C -I -d CallingBell -i RunAll.sql

# API: http://localhost:5080, Swagger at /swagger
cd backend; dotnet run --project src/CallingBell.Api --launch-profile http

# Web: http://localhost:5173, proxies /api and /hubs to VITE_API_PROXY (default http://localhost:5080)
cd web; npm install; npm run dev
npm run build        # tsc -b + vite build (the type check is the lint step: there is no ESLint)

# Tests
dotnet test backend/tests/CallingBell.Seo.Tests
dotnet test backend/tests/CallingBell.Onboarding.Tests
python backend/tests/seo-smoke/seo_smoke.py   # needs the API running
.\tests\RunAllTests.ps1 -Phone 98XXXXXXXX           # everything: unit, build, API, phone OTP, browser (tests/README.md)
```

- **Locked build output:** while the API runs from `bin/Debug`, a second `dotnet build`/`test` fails with file locks. Build to another
  folder (`dotnet build src/CallingBell.Api -o $env:TEMP\cb-build`) or pass `--artifacts-path` to `dotnet test`.
- **Demo accounts:** password `CallingBell@2026`, e.g. `admin@demo.callingbell.in`, `anand.deshmukh@demo.callingbell.in` (owner),
  `vikas.mishra@demo.callingbell.in` (customer).
- **OTP in development:** `Authentication:Otp:ExposeCodeInResponse` is true in `appsettings.Development.json`, so sign-in screens
  show the code. The Development "Log" SMS provider writes a masked line instead of sending.
- **Local AI is optional:** Ollama (`qwen3-embedding:0.6b`, `llama2`) powers semantic search, summaries and the assistant. Everything
  degrades to rules and keywords when it isn't running.

## API conventions

- **Response envelope:** every endpoint returns `ApiResponse<T>`: `{ success, message, data, pagination, errors }`. Controllers use
  `Success(...)`, `Paged(...)`, `Done(...)` from `ApiControllerBase`. Exceptions map to status codes in
  `Api/Infrastructure/ApiInfrastructure.cs`:
  - `ValidationException`: 400 with `errors: { field: [messages] }`.
  - `NotFoundException`: 404.
  - `ConflictException`: 409.
  - `ForbiddenAccessException`: 403.
  - `ExternalServiceException`: the provider's status.
- **Validation:** FluentValidation through the MediatR `ValidationBehaviour`. Field keys like `business.services[0].name` map to form
  paths on the web.
- **Public cache:**
  - Public GET endpoints use output caching (`CachePolicies.PublicCatalog`).
  - `PublicCacheInvalidationBehaviour` clears it, and the SEO cache, after any `*Command` in the Admin, Owner, Onboarding, Payments or
    Engagement features.
  - `ReferenceDataCache` caches cities, areas and the category tree; call `ReferenceDataCache.Invalidate` after changing them.
- **Auth:** JWT plus rotating refresh tokens, roles and permission policies (`Permissions.*`), and rate-limit policies `auth`, `submissions`,
  `public-search` and `assistant` (Program.cs).
- **Database rules:** `AuditableEntity` gives CreatedBy/On, ModifiedBy/On and a soft-delete `IsDeleted` with a global query filter. The audit
  columns are set by `AuditSaveChangesInterceptor`, never by handlers.
- **Tables with triggers** must be declared in their EF mapping (`b.ToTable(t => t.HasTrigger("..."))`). EF Core 8 saves with an
  `OUTPUT` clause that SQL Server rejects on triggered tables. `Businesses` has `TR_Businesses_SlugHistory`.
- **Locations are data:** India stores phone numbers as `+91 98765 43210` (`Phones.Normalize`); providers get E.164
  (`Phones.ToE164`); logs get `Phones.Mask`. All times are IST via `IndianTime`.

## Database scripts

- **Idempotent and numbered:** every script is safe to re-run (guards on `OBJECT_ID` / `COL_LENGTH` / `sys.indexes`, `MERGE` for
  seed rows).
- **Adding a script:** write it as `NN_Name.sql` and add `:r $(ScriptDir)\NN_Name.sql` to `RunAll.sql`. RunAll.sql uses **CRLF** line
  endings, so edit it as bytes. Order matters: `08_Users` runs before the business scripts.
- **Schema with seed:** new tables are created inside their feature script (e.g. `27_Seo`, `28_PopularSearches`, `29_Notifications`,
  `30_JoinCallingBell`), not in `00_Schema`.
- **No hard-coded data:** categories, cities, plans, content, images (`dbo.Media` served as `/api/media/{id}`), popular searches,
  notification routes, providers and templates all live in tables.

## Web conventions

- **Structure:**
  - `src/features/<area>`: pages and feature components.
  - `src/components`: shared UI (`ui.tsx` has `Img` with fallback, `EmptyState`, `ErrorState`, `PageHeader`, `Panel`).
  - `src/lib`: `api.ts` unwraps the envelope and throws `ApiError`; `types.ts` holds all DTO types.
  - `src/stores`: `auth`, `city` (the location selection), `theme`, `ownerBusiness`.
  - Import alias: `@/` → `src/`.
- **Server state:** TanStack Query. Only public, non-personal query keys listed in `PERSISTED` (`lib/queryCache.ts`) are saved to
  localStorage. Never persist Google Maps data (Google's terms) or personal data.
- **Styling:**
  - Use Tailwind utilities with the theme tokens from `index.css` (`bg-surface`, `text-ink`, `text-muted`, `border-line`,
    `bg-accent-soft`, …). They switch with `data-theme` (light/dark) and `data-accent`. Prefer tokens over hex colours.
  - MUI provides inputs, dialogs, menus and grids.
  - Layouts must work at 360 px with no horizontal scroll, and must avoid layout shift (reserve space, use skeletons).
- **Locations:**
  - The visitor's country, state, city and area come from one IP lookup (`GET /api/geo/district`, query key `['geo','location']`).
  - The picker is `CitySelect` in `components/LocationPicker.tsx`, backed by `stores/city.ts`.
- **SEO:**
  - `SeoHead` and `useSeoPage` (`features/seo/seo.tsx`) apply head data from `GET /api/seo`.
  - The API's `SpaSeoMiddleware` renders the same data into `index.html` for crawlers when `Seo:SpaRoot` points at `web/dist`.
- **Verifying UI changes:** run the API and Vite, then drive the pages with Playwright (`playwright-core` + the installed Chrome).
  Check desktop and 390 px, console errors, horizontal overflow and CLS.

## Feature map (where things live)

- **Search:**
  - Calling Bell listings: `Features/Businesses`; search parsing and suggestions: `Features/Search/*`.
  - Google Maps and OpenStreetMap ("AI recommended") tiers: `ExternalSearchFeature`; Google Places: `Infrastructure/Search/GooglePlacesSearch`.
  - Explore nearby: `/nearby` (`features/places`). Its "Popular searches" come from `dbo.PopularSearches`, counted per search.
- **Location:**
  - Country from IP: MaxMind / ip-api, cached per IP.
  - Reverse geocoding: `ReverseGeocoder` + `GeoGrid`.
  - Background workers: `CityCatalogWorker` (GeoNames cities) and `AreaDiscoveryWorker` (Wikidata, then Overpass).
- **Business sign-up:**
  - Wizard: `features/onboarding` (`BusinessWizard`, `steps.tsx`, `schema.ts`). Server side: `Features/Onboarding`.
  - **Join Calling Bell:** `?source=google:…|osm:…&hint=…`. The details come from `GET /api/places/join-calling-bell`; the server
    re-reads the place, cleans it and matches the Calling Bell category, city and area.
  - Duplicate protection: `BusinessDuplicateFinder`, which matches the same source, phone, website or location with a similar name.
  - Claims: `POST /api/businesses/{id}/claim-requests`.
- **Notifications:**
  - In-app: `NotificationPublisher` plus the SignalR `NotificationHub`.
  - Beyond the app: a notification with a `RouteCode` is queued and `NotificationDispatcher` (background service) walks
    `dbo.NotificationRoutingRules`: NEW_LEAD = web push → WhatsApp → RCS → SMS; BOOKING = web push → WhatsApp.
  - Providers live in `Infrastructure/Notifications/Providers`. Provider webhooks update `NotificationDeliveries`; a late failure
    continues the route.
  - OTP goes WhatsApp (authentication template) → SMS (`PhoneOtpService`, `/api/auth/request-otp` and `/api/auth/verify-otp`).
- **SEO:** `Features/Seo` builds pages, schema.org data and sitemaps; it serves `/robots.txt`, `/sitemap*.xml` and `/api/seo`.
- **Payments:** Razorpay checkout and webhook (`Features/Payments`, `Infrastructure/Payments`).

## Secrets and configuration

- **Never commit secrets.** That covers the Google Places key, the JWT key, Razorpay keys, the Firebase service account, WhatsApp and
  SMS tokens, and webhook secrets.
- **Where they go:** `dotnet user-secrets` (the API has a UserSecretsId) in development; environment variables (`Section__Key`)
  or Azure App Service settings / Key Vault elsewhere.
- **What appsettings.json holds:** only non-secret defaults. Optional providers stay `Enabled: false` until configured. The app must
  keep working (degrading gracefully) when Google, Ollama, Firebase, WhatsApp or SMS are not configured.
- **Third-party data rules:**
  - Store only Google place ids, never other Places content (photos, ratings, hours). Photos are proxied through
    `/api/places/photo`, so the key stays on the server.
  - OpenStreetMap data needs attribution.

---

# Calling Bell Platform

## Vision

Calling Bell is a next-generation Real-Time Local Business Discovery, Lead Generation, Booking, Service Marketplace, and Business Growth Ecosystem.

Unlike traditional directories, Calling Bell enables customers to discover businesses, view live availability, communicate instantly, request quotations, book services, and conduct online consultations.

### Tagline

Discover. Connect. Book. Grow.

---

# Product Positioning

Calling Bell is NOT a business directory.

Calling Bell is a:

- Real-Time Local Business Network
- Service Marketplace
- Lead Generation Platform
- Appointment Booking Platform
- Customer Engagement Platform
- Business Growth Ecosystem

Primary Differentiator:

Real-Time Availability Network for local businesses and service providers.

---

# Technology Stack

## Frontend

### Customer Portal

- React 19
- TypeScript
- Vite
- React Router
- Tailwind CSS
- Material UI
- TanStack Query
- Zustand
- React Hook Form
- Zod
- SignalR Client

### Business Portal

- React 19
- TypeScript
- Tailwind CSS
- Material UI
- TanStack Query
- Zustand
- SignalR Client

### Admin Portal

- React 19
- TypeScript
- Material UI
- AG Grid
- Chart.js
- Recharts
- TanStack Query

---

## Backend

### Framework

- .NET 8 Web API

### Architecture

- Clean Architecture
- CQRS Pattern
- MediatR
- Repository Pattern
- Unit Of Work Pattern

### Libraries

- ASP.NET Core Identity
- Entity Framework Core
- MediatR
- FluentValidation
- AutoMapper
- SignalR
- Swagger/OpenAPI

### Security

- JWT Authentication
- Refresh Tokens
- RBAC
- Permission Based Authorization
- Audit Logging
- API Rate Limiting

---

# Database

## Database Engine

SQL Server 2022

Everything must be database-driven.

No hardcoded:

- Categories
- Services
- States
- Cities
- Business Data
- Images
- Subscription Plans
- Advertisements
- Availability Status
- Lookup Data

---

# Image Management

## Rule

All images must come from SQL Server data.

No local hardcoded category images.

No static image arrays.

All portals consume image paths stored in database.

Example:

Categories.ImageUrl
Categories.IconUrl
Categories.BannerUrl

Businesses.LogoUrl
Businesses.CoverImageUrl

BusinessImages.ImageUrl

Services.ImageUrl

Advertisements.ImageUrl

SubscriptionPlans.ImageUrl

---

# User Types

## Customer

Capabilities:

- Search Businesses
- Search Services
- Search By Category
- Search By Location
- Search By Availability
- View Profiles
- Chat
- Call
- Video Consultation
- Book Services
- Submit Reviews
- Save Favorites
- Request Quotations
- Request Callback

---

## Business Owner

Capabilities:

- Business Management
- Staff Management
- Booking Management
- Lead Management
- Advertisement Management
- Availability Management
- Review Management
- Subscription Management
- Analytics Dashboard

---

## Service Provider

Capabilities:

- Availability Control
- Booking Acceptance
- Consultation Management
- Schedule Management
- Customer Chat
- Earnings Tracking

---

## Administrator

Capabilities:

- User Management
- Business Management
- City Management
- Category Management
- Subscription Management
- Advertisement Management
- Revenue Monitoring
- Analytics Dashboard

---

# Core Modules

## Identity Management

Features

- Registration
- Login
- OTP Login
- Social Login
- JWT
- Refresh Token
- Roles
- Permissions
- User Sessions

---

## Business Directory

Features

- Business Listings
- Categories
- Sub Categories
- Services
- Business Images
- Business Videos
- Company Details
- Search

---

## Marketplace

Features

- Service Marketplace
- Quotation Requests
- Lead Generation
- Provider Comparison
- Provider Selection

---

## Booking Management

Features

- Calendar Scheduling
- Instant Booking
- Rescheduling
- Cancellation
- Staff Assignment
- Reminders

---

## Real-Time Availability

Features

- Online Status
- Last Seen
- Chat Availability
- Call Availability
- Video Availability
- Booking Availability

Statuses

- Online
- Offline
- Busy
- Available For Call
- Available For Chat
- Available For Video Consultation
- Available For Booking

---

## Chat Module

Features

- One To One Chat
- Customer Business Chat
- File Sharing
- Image Sharing
- Read Receipts
- Notifications

Technology

SignalR

---

## Reviews Module

Features

- Ratings
- Reviews
- Replies
- Moderation
- Abuse Reporting

---

## Subscription Module

Plans

- Free
- Silver
- Gold
- Platinum
- Enterprise

---

## Advertisement Module

Features

- Featured Listings
- Sponsored Businesses
- Homepage Banner Ads
- Search Result Promotions

---

## Analytics Module

Business Analytics

- Profile Views
- Lead Count
- Booking Count
- Revenue
- Conversion Rate

Admin Analytics

- DAU
- MAU
- Revenue
- Lead Generation
- Bookings
- Active Businesses
- Subscription Revenue

---

# UI / UX Standards

## Design Inspiration

- Airbnb
- Stripe
- LinkedIn
- Urban Company
- Google Business
- Notion

## Avoid

- Bootstrap Look
- Old Dashboard Design
- Heavy Shadows
- Excessive Gradients
- Crowded Screens

---

## Typography

Primary Font:

Inter

Fallback:

Segoe UI, Roboto, Helvetica, Arial, sans-serif

---

# Customer Home Page Layout

1. Hero Search
2. Category Grid
3. Popular Services
4. Nearby Businesses
5. Online Businesses
6. Featured Businesses
7. Sponsored Businesses
8. Recent Reviews
9. Top Rated Businesses
10. Download App Section
11. Footer

---

# Category UI Standard

Each category card must contain:

- Category Image
- Category Icon
- Category Name
- Business Count

Images loaded from database.

Example categories:

- Doctors
- Electricians
- Plumbers
- Lawyers
- Architects
- Tutors
- AC Repair
- Salons
- Beauty Services
- Taxi Services
- Interior Designers
- Hospitals
- Clinics
- Restaurants
- Fitness Trainers
- Yoga Trainers
- Event Planners
- Real Estate

---

# Search Engine

Sources

- SQL Server
- Elasticsearch

Supported Searches

- Electricians Near Me
- Available Doctors
- Online Tutors
- Lawyers Near Me
- AC Repair Nearby
- Salons Open Now

Filters

- Category
- City
- Area
- Rating
- Availability
- Subscription Plan
- Distance

---

# Real-Time Platform

## SignalR Hubs

### PresenceHub

Tracks

- Online Users
- Provider Availability
- Business Availability

### ChatHub

Tracks

- Live Messages
- Typing Indicator
- Read Receipts

### NotificationHub

Tracks

- Alerts
- Booking Notifications
- Lead Notifications

---

# Monetization

Subscription Revenue

- Free
- Silver
- Gold
- Platinum
- Enterprise

Additional Revenue

- Featured Listings
- Banner Advertisements
- Lead Credits
- Booking Commission
- Video Consultation Commission

---

# Development Rules

## Backend

- Clean Architecture
- CQRS
- SOLID Principles
- MediatR
- Validation Using FluentValidation
- Repository Pattern
- Unit Of Work

---

## Frontend

- Feature Based Structure
- TypeScript Strict Mode
- Reusable Components
- Responsive Design
- Mobile First

---

## Database Standards

- SQL Server
- Proper Foreign Keys
- Soft Deletes
- Audit Columns
- CreatedBy
- CreatedOn
- ModifiedBy
- ModifiedOn

---

# Future Roadmap

## Phase 1

- Directory
- Search
- Leads
- Bookings

## Phase 2

- Real-Time Availability
- Chat
- Notifications
- Staff Management

## Phase 3

- Video Consultation
- AI Recommendation Engine
- Smart Lead Matching

## Phase 4

- Android App
- iOS App
- Multi Country Support
- Enterprise Expansion


## Portal Development Requirements

### 1. Prepare Realistic SQL Data

Prepare production-like, realistic sample/master data for the portal and create complete SQL Server scripts to insert the data into the appropriate database tables.

The SQL scripts should:

* Create realistic business, category, location, service, user, banner, review, enquiry, booking, subscription, advertisement, and other required data.
* Use meaningful Indian/Indian-city-based business names, addresses, phone numbers, email addresses, service descriptions, prices, ratings, and operating hours.
* Include multiple cities, areas, localities, and pincodes.
* Create sufficient data to properly demonstrate all portal screens and features.
* Maintain correct foreign-key relationships between tables.
* Avoid duplicate or meaningless dummy values such as `Test Business`, `ABC`, `Lorem Ipsum`, etc.
* Use realistic but clearly non-production contact information where appropriate.
* Include active/inactive records and different business statuses to demonstrate filtering and management functionality.
* Include different business categories and subcategories.
* Include realistic images/URLs or image placeholders compatible with the existing image architecture.
* Include realistic reviews, ratings, enquiries, bookings, advertisements, subscriptions, and lead data.
* Include created/updated dates that represent realistic historical activity.

### 2. SQL Script Structure

Create separate, well-organized SQL scripts where appropriate:

* `01_MasterData.sql`
* `02_Locations.sql`
* `03_Categories.sql`
* `04_Businesses.sql`
* `05_BusinessServices.sql`
* `06_Banners.sql`
* `07_Reviews.sql`
* `08_Users.sql`
* `09_Enquiries.sql`
* `10_Bookings.sql`
* `11_Advertisements.sql`
* `12_Subscriptions.sql`
* `13_DashboardDemoData.sql`

The scripts must be:

* SQL Server compatible.
* Idempotent where practical.
* Safe to execute repeatedly without unnecessarily creating duplicate records.
* Ordered according to table dependencies.
* Consistent with the existing database schema.
* Properly escaped and parameter-safe.
* Easy for a developer to execute in SQL Server Management Studio or Azure Data Studio.

If the existing tables already contain data, inspect the schema first and generate inserts based on the actual table and column structure rather than inventing incompatible columns.

### 3. Populate All Portal Screens

Ensure the inserted data is sufficient to make every portal page look complete.

The portal should not display:

* Empty dashboards
* Empty cards
* Empty tables
* Broken images
* Placeholder business names
* `undefined`
* `null`
* `N/A` where meaningful data should exist
* Hardcoded frontend demo data

All displayed business/category/location/dashboard information should come from the SQL Server database through the existing API layer.

### 4. Modern Professional UI

Redesign/refine the portal to have a modern, professional enterprise-quality appearance while preserving the existing functionality.

The design should feel like a real commercial product rather than a basic CRUD application.

Use:

* Clean modern layouts
* Professional typography
* Consistent spacing
* Responsive grids
* Modern cards
* High-quality icons
* Clear visual hierarchy
* Professional tables
* Modern forms
* Search and filtering
* Status badges
* Statistics/KPI cards
* Charts where appropriate
* Responsive navigation
* Proper empty/loading/error states
* Consistent buttons and controls
* Accessible color contrast
* Smooth but subtle interactions

Avoid:

* Excessive gradients
* Excessive shadows
* Overloaded dashboards
* Large unnecessary animations
* Generic template styling
* Inconsistent colors
* Excessive rounded cards
* Cluttered layouts

### 5. Portal Branding

Follow the existing portal branding and design system.

Use a professional color palette based around:

* Primary: `#0B1220`
* Secondary: `#021223`
* Accent: `#F4A62C`
* Background: `#F7F8FA`
* White: `#FFFFFF`
* Text: `#161616`
* Muted text: `#667085`
* Border: `#E5E7EB`

The design should look premium, trustworthy, modern, and suitable for a commercial local-services platform.

### 6. Dashboard

Create a professional dashboard containing realistic data such as:

* Total Businesses
* Active Businesses
* New Businesses
* Total Customers
* Total Leads
* New Enquiries
* Total Bookings
* Pending Bookings
* Revenue
* Active Subscriptions
* Advertisements
* Reviews
* Average Rating

Add appropriate charts for:

* Business registrations over time
* Leads over time
* Bookings over time
* Revenue trends
* Category distribution
* City/location distribution
* Subscription distribution

All dashboard statistics and charts must be dynamically loaded from the API/database.

### 7. Business Management

The business management screens should support realistic data and provide:

* Business listing
* Search
* Category filtering
* City/area filtering
* Status filtering
* Verification status
* Subscription status
* Business details
* Services
* Contact information
* Working hours
* Images
* Reviews
* Leads
* Bookings
* Advertisement status

Use professional data tables with pagination, sorting, filtering and responsive behavior.

### 8. Customer Experience

The customer-facing portal should provide a polished experience for:

* Searching businesses
* Browsing categories
* Finding nearby businesses
* Viewing business details
* Viewing services
* Checking availability
* Reading reviews
* Sending enquiries
* Booking services
* Contacting businesses
* Viewing offers
* Viewing sponsored businesses

### 9. API/Data Architecture

Do not hardcode production-like data inside React/TypeScript components.

Use the architecture:

**SQL Server → .NET API → React/TypeScript UI**

The frontend should retrieve data through APIs.

Use the existing API conventions and response structure where applicable:

```json
{
  "success": true,
  "message": "Success",
  "data": {},
  "pagination": {}
}
```

Use Dapper/stored procedures where that is already part of the project architecture.

### 10. Images

Use the existing image architecture.

Business/category/banner images should support:

* `ImageUrl`
* `ThumbnailUrl`
* `MobileImageUrl`
* `DesktopImageUrl`
* `AltText`
* `IsPrimary`

Provide graceful fallback images when an image is unavailable.

Do not allow broken image icons to appear in the UI.

### 11. Responsive Design

The entire portal must work correctly on:

* Desktop
* Laptop
* Tablet
* Mobile

Check:

* Navigation
* Tables
* Cards
* Forms
* Search
* Filters
* Dashboards
* Charts
* Modals
* Business details
* Images

Avoid horizontal scrolling wherever possible.

### 12. Professional UX

Add appropriate:

* Loading skeletons
* Toast notifications
* Confirmation dialogs
* Validation messages
* Error handling
* Empty states
* Success states
* Pagination
* Search debounce
* Filter reset
* Breadcrumbs
* Tooltips
* Hover states
* Keyboard-friendly controls

Do not introduce unnecessary animations that cause flickering or layout shifts.

### 13. Important Implementation Rule

Before changing the database or UI:

1. Inspect the existing project structure.
2. Inspect the existing SQL Server schema.
3. Inspect existing API endpoints.
4. Inspect existing React components and routing.
5. Reuse existing components and functionality where appropriate.
6. Do not unnecessarily change working APIs.
7. Do not remove existing features.
8. Do not introduce duplicate components.
9. Keep the implementation modular and maintainable.

### 14. Final Validation

After implementation, verify:

* SQL scripts execute successfully.
* Foreign keys are valid.
* No duplicate demo records are unnecessarily created.
* API endpoints return real database data.
* React screens display the API data correctly.
* No hardcoded business/category data remains where database data is expected.
* No broken images exist.
* No console errors exist.
* No API 404/500 errors exist.
* No layout flickering occurs.
* No major layout shifts occur.
* All routes work.
* Search and filters work.
* Pagination works.
* Dashboard statistics are accurate.
* The portal is responsive.
* The final UI looks like a production-ready commercial application.

### Expected Result

The final result should be a **fully populated, modern, professional, production-style portal** backed by realistic SQL Server data.

It should not look like a prototype or CRUD demo.

The application should demonstrate the complete business flow:

**Discover → Search → View Business → Enquire → Book → Review → Manage → Advertise → Analyze**

Use real database-driven data throughout the application and ensure the UI, API, and SQL database work together as one complete system.

Business Lead Registration Flow

1. During account creation, the Business Lead must provide the following information:
   - Business Name
   - Business Category
   - Industry Type
   - Business Description
   - Contact Information
   - Website URL
   - Business Address
   - Available Business Plans/Packages
   - Services Offered
   - Social Media Links

2. Allow the Business Lead to upload:
   - Business Logo
   - Cover Image/Banner
   - Business Photos (multiple uploads)
   - Promotional Videos (multiple uploads)

3. Validate all mandatory fields before account creation.

4. After successful registration and profile setup:
   - Create the business profile automatically.
   - Save all uploaded images and videos.
   - Redirect the user to the Business Dashboard.

5. Business Dashboard should display:
   - Business Overview
   - Uploaded Photos and Videos
   - Active Plan Details
   - Lead Management
   - Analytics and Insights
   - Profile Completion Status
   - Recent Activities
   - Settings and Account Management

6. UI Requirements:
   - Modern and Professional Design
   - Responsive Layout
   - Multi-step Registration Wizard
   - Drag-and-Drop Media Upload
   - Progress Indicator During Registration
   - Clean Dashboard with Cards, Charts, and Metrics