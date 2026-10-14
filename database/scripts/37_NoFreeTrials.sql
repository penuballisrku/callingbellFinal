/* =====================================================================
   37_NoFreeTrials.sql
   CallInBell has no free trials: a paid plan starts only once it is paid for (Razorpay). Earlier seed data gave businesses awaiting
   approval a 14-day "Trial" of a paid plan, which the owner dashboard showed as "N days left in your trial". This moves every
   subscription still in Trial to the Free plan (a basic listing that doesn't expire). A business that already has another current
   plan keeps that plan and the trial is cancelled instead.

   Idempotent: only rows still in Trial are touched. Safe to run on its own against any database, e.g.
     sqlcmd -S .\SQLEXPRESS -E -C -I -d CallingBell_Prod -i 37_NoFreeTrials.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @FreePlanId uniqueidentifier = (SELECT Id FROM dbo.SubscriptionPlans WHERE Code = N'FREE');
DECLARE @Today date = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS date);

IF @FreePlanId IS NULL
BEGIN
    RAISERROR(N'37_NoFreeTrials: the FREE plan is missing (run 12_Subscriptions first).', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

-- The business already has another current plan: keep it, cancel the trial.
UPDATE s
SET Status = N'Cancelled', ModifiedBy = N'system', ModifiedOn = SYSDATETIMEOFFSET()
FROM dbo.BusinessSubscriptions s
WHERE s.Status = N'Trial'
  AND EXISTS (SELECT 1 FROM dbo.BusinessSubscriptions o
              WHERE o.BusinessId = s.BusinessId AND o.Id <> s.Id AND o.Status = N'Active' AND o.IsDeleted = 0
                AND o.StartDate <= @Today AND o.EndDate >= @Today);

-- Otherwise the trial becomes a Free listing.
UPDATE dbo.BusinessSubscriptions
SET PlanId = @FreePlanId, Status = N'Active', Amount = 0, AutoRenew = 0, EndDate = DATEADD(YEAR, 10, StartDate),
    ModifiedBy = N'system', ModifiedOn = SYSDATETIMEOFFSET()
WHERE Status = N'Trial';

COMMIT TRANSACTION;

DECLARE @Remaining int = (SELECT COUNT(*) FROM dbo.BusinessSubscriptions WHERE Status = N'Trial');
PRINT CONCAT(N'37_NoFreeTrials: subscriptions still in Trial: ', @Remaining);
