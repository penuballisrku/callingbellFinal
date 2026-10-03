// Mirrors the API DTOs (camelCase JSON).

export interface Pagination { page: number; pageSize: number; totalCount: number; totalPages: number }
export interface ApiEnvelope<T> {
  success: boolean;
  message: string;
  data: T;
  pagination?: Pagination | null;
  errors?: Record<string, string[]> | null;
}
export interface Paged<T> { items: T[]; pagination: Pagination }

export interface CurrentUser {
  id: string; email: string; displayName: string; phoneNumber?: string | null; avatarUrl?: string | null;
  userType: 'Customer' | 'BusinessOwner' | 'ServiceProvider' | 'Administrator';
  citySlug?: string | null; roles: string[]; permissions: string[];
}
export interface AuthResult { accessToken: string; accessTokenExpiresAt: string; refreshToken: string; user: CurrentUser }

export interface Lookup { code: string; name: string; description?: string | null; colorHex?: string | null; sortOrder: number }
export type Lookups = Record<string, Lookup[]>;

export interface SubCategory {
  id: string; name: string; slug: string; description?: string | null; imageUrl?: string | null; iconUrl?: string | null; altText?: string | null;
  isFeatured: boolean; businessCount: number; categorySlug: string; categoryName: string; colorHex?: string | null;
}
export interface Category {
  id: string; name: string; slug: string; description?: string | null; imageUrl?: string | null; iconUrl?: string | null; bannerUrl?: string | null;
  altText?: string | null; colorHex?: string | null; isFeatured: boolean; businessCount: number; subCategories: SubCategory[];
}
export interface Area { id: string; name: string; slug: string; pincode: string }
/** A city's area with search aliases: alternate spellings and the sub-localities (neighbourhoods) inside it. */
export interface CityArea { id: string; name: string; slug: string; pincode: string; areaType?: string | null; altNames: string[]; subLocalities: string[] }
export interface CityAreas { citySlug: string; cityName: string; state: string; discovering: boolean; discoveredOn?: string | null; subLocalityCount: number; areas: CityArea[] }
export interface City { id: string; name: string; slug: string; state: string; imageUrl?: string | null; isPopular: boolean; businessCount: number; areas: Area[] }
export interface Plan {
  id: string; code: string; name: string; tagline?: string | null; monthlyPrice: number; annualPrice: number; leadCredits: number;
  includesFeaturedListing: boolean; includesPrioritySupport: boolean; features: string[]; imageUrl?: string | null; badgeColor?: string | null; isPopular: boolean;
  maxServices: number; maxImages: number;
}
export type SuggestionKind = 'Category' | 'SubCategory' | 'Service' | 'Business';
export interface SearchSuggestion { kind: SuggestionKind; label: string; detail?: string | null; imageUrl?: string | null; slug: string; subCategorySlug?: string | null; rating?: number | null }
export interface SearchSuggestions { query: string; categories: SearchSuggestion[]; services: SearchSuggestion[]; businesses: SearchSuggestion[] }

export interface Banner {
  id: string; title: string; subtitle?: string | null; ctaText?: string | null; linkUrl?: string | null; imageUrl: string;
  mobileImageUrl?: string | null; desktopImageUrl?: string | null; altText?: string | null; placement: string;
}

/** Marketing page content (e.g. "List your business"), grouped by `section`. */
export interface ContentBusiness {
  name: string; slug: string; city: string; logoUrl?: string | null; categoryName?: string | null; averageRating: number; reviewCount: number;
  isVerified: boolean; leads: number; bookings: number; planName?: string | null;
}
export interface ContentBlock {
  code: string; section: string; eyebrow?: string | null; title: string; subtitle?: string | null; body?: string | null; iconKey?: string | null;
  imageUrl?: string | null; thumbnailUrl?: string | null; mobileImageUrl?: string | null; desktopImageUrl?: string | null; altText?: string | null;
  videoUrl?: string | null; mediaCredit?: string | null; mediaCreditUrl?: string | null; ctaText?: string | null; linkUrl?: string | null;
  business?: ContentBusiness | null;
}
export interface BusinessGrowthStats {
  activeBusinesses: number; verifiedBusinesses: number; cities: number; categories: number; leadsLast30Days: number;
  bookingsCompleted: number; reviews: number; averageRating: number;
}
export interface MarketingPage { pageKey: string; blocks: ContentBlock[]; stats: BusinessGrowthStats }

export interface BusinessCard {
  id: string; slug: string; name: string; tagline?: string | null; logoUrl?: string | null; coverImageUrl?: string | null;
  categoryName: string; categorySlug: string; subCategoryName?: string | null; subCategorySlug?: string | null;
  city: string; area?: string | null; averageRating: number; reviewCount: number; availabilityStatus: string; lastSeenOn?: string | null;
  isVerified: boolean; isFeatured: boolean; isSponsored: boolean; startingPrice?: number | null; acceptsOnlineBooking: boolean;
  offersVideoConsultation: boolean; offersHomeService: boolean; isOpenNow: boolean; responseTimeMinutes?: number | null;
  planCode?: string | null; distanceKm?: number | null;
}

export interface PopularService {
  name: string; searchTerm: string;
  subCategoryName: string; subCategorySlug: string; categoryName: string; categorySlug: string; colorHex?: string | null;
  imageUrl?: string | null; iconUrl?: string | null; altText?: string | null;
  startingPrice: number; priceUnit?: string | null; rating: number; reviewCount: number; bookingCount: number;
}
/** A popular service near the visitor; `reason` is the local AI model's one-line explanation once its ranking is ready. */
export interface NearbyService extends Omit<PopularService, 'altText'> { altText?: string | null; nearestKm?: number | null; reason?: string | null }
export interface NearbyServices {
  placeName?: string | null; cityName?: string | null; citySlug?: string | null; source: 'area' | 'ip' | 'city' | 'none';
  aiRanked: boolean; aiPending: boolean; aiModel?: string | null; items: NearbyService[]; relatedCategories: RelatedCategory[];
}
/** A sub-category that complements the popular services nearby; `reason` comes from the local AI model once ready. */
export interface RelatedCategory {
  name: string; slug: string; categoryName: string; categorySlug: string; colorHex?: string | null; iconUrl?: string | null; imageUrl?: string | null;
  businessCount: number; nearestKm?: number | null; reason?: string | null;
}
/** A category in the visitor's city for "Top picks": `source` is "database" (ranked from local data) or "ai" (added by the AI for this city). */
export interface TopPickCategory {
  name: string; slug: string; categoryName: string; categorySlug: string; colorHex?: string | null; iconUrl?: string | null;
  businessCount: number; rating: number; bookingCount: number; source: 'database' | 'ai'; reason?: string | null;
}
export interface TopPicks {
  cityName?: string | null; citySlug?: string | null; placeName?: string | null; locationSource: 'ip' | 'city' | 'none'; totalInCity: number;
  aiEnriched: boolean; aiPending: boolean; aiModel?: string | null; categories: TopPickCategory[];
}
/** A real customer review near the visitor; `customerName` is first name + last initial. */
export interface LocalReview {
  id: string; customerName: string; rating: number; title?: string | null; comment: string; createdOn: string; isVerifiedVisit: boolean;
  businessName: string; businessSlug: string; businessLogoUrl?: string | null; subCategoryName?: string | null; area?: string | null; city: string;
}
export interface LocalReviews {
  placeName?: string | null; cityName?: string | null; scope: 'area' | 'city' | 'all'; reviewCount: number; averageRating: number;
  aiSummary?: string | null; aiPending: boolean; reviews: LocalReview[];
}
export interface HomeData {
  citySlug?: string | null; cityName?: string | null; heroBanners: Banner[]; promoBanners: Banner[]; categories: SubCategory[];
  popularServices: PopularService[];
  stats: { businesses: number; cities: number; reviews: number; bookingsCompleted: number };
}

export interface AppliedFilters { q?: string | null; categorySlug?: string | null; categoryName?: string | null; subSlug?: string | null; subName?: string | null; citySlug?: string | null; cityName?: string | null; openNow: boolean; availability?: string | null; areaName?: string | null; areaMatches?: number | null }

export interface Service { id: string; name: string; description: string; price: number; priceUnit?: string | null; durationMinutes: number; type: string; imageUrl?: string | null; isPopular: boolean }
export interface Hours { dayOfWeek: number; day: string; open?: string | null; close?: string | null; isClosed: boolean; isToday: boolean }
export interface BusinessImage { id: string; imageUrl: string; thumbnailUrl?: string | null; mobileImageUrl?: string | null; desktopImageUrl?: string | null; altText?: string | null; caption?: string | null; isPrimary: boolean }
export interface Review { id: string; rating: number; title?: string | null; comment: string; customerName: string; createdOn: string; ownerReply?: string | null; repliedOn?: string | null; isVerifiedVisit: boolean; helpfulCount: number }
export interface BusinessDetail {
  card: BusinessCard; description: string; addressLine?: string | null; landmark?: string | null; pincode?: string | null;
  latitude?: number | null; longitude?: number | null; phoneNumber?: string | null; whatsAppNumber?: string | null; email?: string | null; website?: string | null;
  yearEstablished?: number | null; teamSize?: number | null; languages?: string | null; verifiedOn?: string | null; categoryColor?: string | null; citySlug?: string | null;
  planName?: string | null; isFavorite: boolean; services: Service[]; hours: Hours[]; images: BusinessImage[]; ratingBreakdown: Record<string, number>;
  videos: BusinessVideo[]; socialLinks: SocialLink[];
  recentReviews: Review[]; similar: BusinessCard[];
}
export interface Slot { time: string; start: string; available: boolean }
export interface CreatedReference { id: string; reference: string }

export interface MyBooking {
  id: string; bookingNumber: string; businessName: string; businessSlug: string; businessLogoUrl?: string | null; serviceName: string;
  scheduledStart: string; scheduledEnd: string; status: string; amount: number; paymentStatus: string; serviceAddress?: string | null;
  businessPhone?: string | null; canCancel: boolean; canReview: boolean;
}
export interface MyEnquiry { id: string; enquiryNumber: string; businessName: string; businessSlug: string; serviceName?: string | null; enquiryType: string; message: string; status: string; quotedAmount?: number | null; createdOn: string; respondedOn?: string | null }
export interface NotificationItem { id: string; title: string; message: string; notificationType: string; linkUrl?: string | null; isRead: boolean; createdOn: string }

// ---------- Business portal ----------
export interface Kpi { key: string; label: string; value: number; changePercent?: number | null; format: 'number' | 'currency' | 'percent' | 'rating' }
export interface OwnerBusiness { id: string; name: string; slug: string; logoUrl?: string | null; city: string; area?: string | null; status: string; verificationStatus: string; availabilityStatus: string; planName?: string | null; averageRating: number; reviewCount: number; categoryName: string }
export interface OwnerLead { id: string; enquiryNumber: string; customerName: string; customerPhone: string; customerEmail?: string | null; enquiryType: string; serviceName?: string | null; message: string; status: string; source: string; budget?: number | null; quotedAmount?: number | null; preferredDate?: string | null; createdOn: string; respondedOn?: string | null }
export interface OwnerBooking { id: string; bookingNumber: string; customerName: string; customerPhone: string; serviceName: string; scheduledStart: string; scheduledEnd: string; status: string; amount: number; paymentStatus: string; serviceAddress?: string | null; notes?: string | null; cancellationReason?: string | null; createdOn: string }
export interface NameCount { name: string; count: number }
export interface OwnerDashboard {
  business: OwnerBusiness; kpis: Kpi[];
  weekly: { week: string; weekStart: string; views: number; leads: number; bookings: number; revenue: number }[];
  leadSources: NameCount[]; leadStatuses: NameCount[]; ratingBreakdown: Record<string, number>;
  recentLeads: OwnerLead[]; upcomingBookings: OwnerBooking[];
  plan?: { code: string; name: string; leadCredits: number; leadsThisMonth: number; renewsOn?: string | null; billingCycle: string } | null;
}
export interface OwnerList<T> { page: { items: T[]; meta: Pagination }; statusCounts: Record<string, number> }
export interface OwnerReview { id: string; rating: number; title?: string | null; comment: string; customerName: string; status: string; ownerReply?: string | null; repliedOn?: string | null; isVerifiedVisit: boolean; createdOn: string }
export interface OwnerService { id: string; name: string; description: string; price: number; priceUnit?: string | null; durationMinutes: number; type: string; isPopular: boolean; isActive: boolean; bookingCount: number }
export interface OwnerProfile { id: string; name: string; tagline?: string | null; description: string; phoneNumber?: string | null; whatsAppNumber?: string | null; email?: string | null; website?: string | null; addressLine?: string | null; landmark?: string | null; acceptsOnlineBooking: boolean; offersVideoConsultation: boolean; offersHomeService: boolean; hours: Hours[] }
export interface SubscriptionHistory { subscriptionNumber: string; planName: string; billingCycle: string; startDate: string; endDate: string; amount: number; status: string }
export interface Invoice { invoiceNumber: string; paymentType: string; amount: number; taxAmount: number; totalAmount: number; paymentMode: string; status: string; paidOn: string }
export interface OwnerSubscription { current?: SubscriptionHistory | null; currentPlanCode?: string | null; plans: Plan[]; history: SubscriptionHistory[]; invoices: Invoice[] }
export interface OwnerAd { id: string; campaignCode: string; adType: string; title: string; description?: string | null; startDate: string; endDate: string; budget: number; amountSpent: number; impressions: number; clicks: number; ctr: number; status: string; targetCity?: string | null; targetCategory?: string | null }

// ---------- Media, social links & onboarding ----------
export interface BusinessVideo { id: string; title: string; videoUrl: string; posterUrl?: string | null; durationSeconds?: number | null }
export interface SocialLink { platform: string; url: string }
export type MediaKind = 'logo' | 'cover' | 'photo' | 'video';
export interface OwnerMediaItem {
  id: string; kind: MediaKind; url: string; thumbnailUrl?: string | null; title?: string | null; contentType?: string | null; fileSize?: number | null;
  durationSeconds?: number | null; isPrimary: boolean; createdOn: string;
}
export interface MediaLimits { planName: string; maxPhotos: number; maxVideos: number; maxImageMb: number; maxVideoMb: number }
export interface OwnerMedia { logoUrl?: string | null; coverImageUrl?: string | null; photos: OwnerMediaItem[]; videos: OwnerMediaItem[]; limits: MediaLimits }
export interface CreatedBusiness {
  businessId: string; slug: string; status: string; planName: string; subscriptionStatus: string;
  /** Set when a paid plan was chosen: the business starts on Free and checkout follows. */
  requestedPlanCode?: string | null; requestedPlanName?: string | null; billingCycle: string;
}
export interface PaymentConfig { enabled: boolean; gateway: string; keyId?: string | null; gstRatePercent: number }
export interface CheckoutOrder {
  orderId: string; orderNumber: string; gateway: string; keyId: string; gatewayOrderId: string; amountInPaise: number; currency: string;
  planCode: string; planName: string; billingCycle: string; subtotal: number; tax: number; total: number; businessName: string;
  prefill: { name: string; email: string; contact: string };
}
export interface PaymentResult {
  orderId: string; orderNumber: string; status: 'Created' | 'Paid' | 'Failed' | 'Cancelled'; planName: string; billingCycle: string;
  activeFrom?: string | null; activeUntil?: string | null; invoiceNumber?: string | null; total: number; paymentMethod?: string | null; failureReason?: string | null;
}
export interface PendingPayment { orderId: string; planCode: string; planName: string; billingCycle: string; total: number; status: string; failureReason?: string | null; createdOn: string }
export interface BusinessRegistrationResult { auth: AuthResult; business: CreatedBusiness }

export interface OwnerBusinessCard {
  id: string; name: string; slug: string; tagline?: string | null; logoUrl?: string | null; coverImageUrl?: string | null; categoryName: string;
  subCategoryName?: string | null; city: string; area?: string | null; status: string; verificationStatus: string; phoneNumber?: string | null;
  email?: string | null; website?: string | null; serviceCount: number; createdOn: string;
}
export interface CompletionItem { key: string; label: string; hint: string; done: boolean; linkUrl: string }
export interface ActivePlan {
  code: string; name: string; status: string; billingCycle: string; startDate: string; endDate: string; amount: number; leadCredits: number;
  leadsThisMonth: number; maxServices: number; maxImages: number; features: string[]; trialDaysLeft?: number | null;
}
export interface Activity { type: string; title: string; description: string; occurredOn: string; linkUrl?: string | null }
export interface OwnerOverview {
  business: OwnerBusinessCard; completion: { percent: number; completed: number; total: number; items: CompletionItem[] }; plan?: ActivePlan | null;
  recentPhotos: OwnerMediaItem[]; videos: OwnerMediaItem[]; photoCount: number; activities: Activity[]; pendingPayment?: PendingPayment | null;
}
export interface AccountSettings {
  displayName: string; email: string; phoneNumber?: string | null; hasPassword: boolean; googleLinked: boolean; createdOn: string;
  lastLoginOn?: string | null; activeSessions: number;
}

// ---------- Admin ----------
export interface MonthlyPoint { month: string; monthStart: string; values: Record<string, number> }
export interface Distribution { key: string; name: string; count: number; value?: number | null; colorHex?: string | null }
export interface AdminDashboard {
  kpis: Kpi[]; registrations: MonthlyPoint[]; leads: MonthlyPoint[]; bookings: MonthlyPoint[]; revenue: MonthlyPoint[];
  activeUsers: { date: string; dau: number; mau: number; newUsers: number }[];
  categories: Distribution[]; cities: Distribution[]; subscriptions: Distribution[];
  attention: { pendingApprovals: number; pendingVerifications: number; flaggedReviews: number; pendingAds: number };
}
export interface AdminBusinessRow {
  id: string; name: string; slug: string; logoUrl?: string | null; categoryName: string; subCategoryName?: string | null; city: string; area?: string | null;
  ownerName: string; ownerEmail?: string | null; phoneNumber?: string | null; status: string; verificationStatus: string; isFeatured: boolean; planCode?: string | null;
  averageRating: number; reviewCount: number; leads30: number; bookings30: number; liveAds: number; availabilityStatus: string; createdOn: string;
}
export interface AdminBusinessDetail {
  summary: AdminBusinessRow;
  profile: { tagline?: string | null; description: string; addressLine?: string | null; landmark?: string | null; pincode?: string | null; phoneNumber?: string | null; whatsAppNumber?: string | null; email?: string | null; website?: string | null; yearEstablished?: number | null; teamSize?: number | null; languages?: string | null; coverImageUrl?: string | null; verifiedOn?: string | null; ownerPhone?: string | null; ownerLastLogin?: string | null };
  services: Service[]; hours: Hours[]; images: BusinessImage[]; recentLeads: OwnerLead[]; recentBookings: OwnerBooking[]; recentReviews: OwnerReview[];
  advertisements: OwnerAd[]; subscriptions: SubscriptionHistory[];
  totals: { leads: number; convertedLeads: number; bookings: number; completedBookings: number; bookingRevenue: number; platformRevenue: number; profileViews30: number };
}
export interface AdminCategory { id: string; name: string; slug: string; description?: string | null; imageUrl?: string | null; iconUrl?: string | null; colorHex?: string | null; isActive: boolean; isFeatured: boolean; sortOrder: number; businessCount: number; leadCount30: number; subCategories: { id: string; name: string; slug: string; iconUrl?: string | null; isActive: boolean; isFeatured: boolean; sortOrder: number; businessCount: number }[] }
export interface AdminCity { id: string; name: string; slug: string; state: string; imageUrl?: string | null; isActive: boolean; isPopular: boolean; areaCount: number; businessCount: number; customerCount: number; leads30: number }
export interface AdminUser { id: string; displayName: string; email?: string | null; phoneNumber?: string | null; userType: string; city?: string | null; isActive: boolean; createdOn: string; lastLoginOn?: string | null; bookings: number; reviews: number; businesses: number }
export interface AdminReview { id: string; rating: number; title?: string | null; comment: string; status: string; reportReason?: string | null; customerName: string; customerEmail?: string | null; businessName: string; businessSlug: string; city: string; createdOn: string }
export interface AdminAd { ad: OwnerAd; businessId: string; businessName: string; businessSlug: string; city: string }
export interface AdminSubscriptions {
  plans: { code: string; name: string; badgeColor?: string | null; activeCount: number; mrr: number; revenue12m: number }[];
  totalMrr: number; expiringIn30Days: number;
  current: { items: { subscriptionNumber: string; businessId: string; businessName: string; city: string; planCode: string; planName: string; billingCycle: string; startDate: string; endDate: string; amount: number; status: string; autoRenew: boolean }[]; meta: Pagination };
}

/** A real place from outside the platform (OpenStreetMap or Google Maps), shown below registered businesses in search. */
export interface ExternalPlace {
  id: string; name: string; kind?: string | null; address?: string | null; phone?: string | null; website?: string | null; openingHours?: string | null;
  rating?: number | null; ratingCount?: number | null; distanceKm: number; directionsUrl: string; sourceUrl?: string | null;
}
/** status: ready | off (not configured) | unavailable | skipped. aiStatus (AI tier): ranked | pending (poll) | off. */
export interface ExternalTier { status: 'ready' | 'off' | 'unavailable' | 'skipped'; aiStatus?: 'ranked' | 'pending' | 'off' | null; total: number; duplicates: number; items: ExternalPlace[] }
export interface ExternalSearch { query?: string | null; placeName?: string | null; cityName?: string | null; origin?: string | null; ai: ExternalTier; google: ExternalTier }
