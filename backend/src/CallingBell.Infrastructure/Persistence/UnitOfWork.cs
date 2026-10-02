using System.Collections.Concurrent;
using CallingBell.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Infrastructure.Persistence;

internal sealed class Repository<T>(ApplicationDbContext context) : IRepository<T> where T : class
{
    private readonly DbSet<T> _set = context.Set<T>();

    public IQueryable<T> Query() => _set;
    public IQueryable<T> QueryNoTracking() => _set.AsNoTracking();
    public void Add(T entity) => _set.Add(entity);
    public void Remove(T entity) => _set.Remove(entity);
}

internal sealed class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    public IRepository<T> Repository<T>() where T : class =>
        (IRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new Repository<T>(context));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);

    // SQL retries are enabled, so a user transaction must run inside the execution strategy.
    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            context.ChangeTracker.Clear(); // a retry starts from a clean slate
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
}
