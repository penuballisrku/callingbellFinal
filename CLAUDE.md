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
