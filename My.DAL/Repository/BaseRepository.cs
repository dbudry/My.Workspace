using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using My.DAL.Data;
using My.DAL.Models.Paging;

namespace My.DAL.Repository
{
    public class BaseRepository<TEntity> : IRepository<TEntity> where TEntity : class
    {
        internal ApplicationDbContext context;
        internal DbSet<TEntity> dbSet;

        public BaseRepository(ApplicationDbContext context)
        {
            this.context = context;
            dbSet = context.Set<TEntity>();
        }

        public async Task Delete(TEntity entityToDelete, CancellationToken ct = default)
        {
            if (context.Entry(entityToDelete).State == EntityState.Detached)
            {
                dbSet.Attach(entityToDelete);
            }

            dbSet.Remove(entityToDelete);
            await context.SaveChangesAsync(ct);
        }

        public async Task Delete(object id, CancellationToken ct = default)
        {
            TEntity? entityToDelete = await dbSet.FindAsync(new[] { id }, ct);

            if (entityToDelete != null)
                await Delete(entityToDelete, ct);
        }

        public async Task<TEntity?> Find(object id, CancellationToken ct = default)
        {
            return await dbSet.FindAsync(new[] { id }, ct);
        }

        public async Task<PagedList<TEntity>> GetPaged(
            PagingParameters parameters,
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            string includeProperties = "",
            CancellationToken ct = default)
        {
            // Count on the filtered root set only — includes and ORDER BY are not needed
            // for TotalCount and can force expensive joins on list endpoints.
            IQueryable<TEntity> filtered = dbSet.AsNoTracking();
            if (filter != null)
                filtered = filtered.Where(filter);

            var count = await filtered.CountAsync(ct);

            IQueryable<TEntity> query = filtered;
            if (!string.IsNullOrEmpty(includeProperties))
            {
                foreach (var includeProperty in includeProperties.Split
                (new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    query = query.Include(includeProperty.Trim());
                }

                if (includeProperties.Contains(','))
                    query = query.AsSplitQuery();
            }

            if (orderBy != null)
                query = orderBy(query);

            var items = await query
                .Skip((parameters.PageNumber - 1) * parameters.PageSize)
                .Take(parameters.PageSize)
                .ToListAsync(ct);

            return new PagedList<TEntity>(items, count, parameters.PageNumber, parameters.PageSize);
        }

        public async Task<IEnumerable<TEntity>> Get(Expression<Func<TEntity, bool>>? filter = null, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, string includeProperties = "", CancellationToken ct = default)
        {
            IQueryable<TEntity> query = dbSet;

            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (!string.IsNullOrEmpty(includeProperties))
            {
                foreach (var includeProperty in includeProperties.Split
                (new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    query = query.Include(includeProperty.Trim());
                }
            }

            if (orderBy != null)
            {
                return await orderBy(query).ToListAsync(ct);
            }
            else
            {
                return await query.ToListAsync(ct);
            }
        }

        public async Task<IEnumerable<TType>> Get<TType>(Expression<Func<TEntity, TType>> select, Expression<Func<TEntity, bool>>? where = null, CancellationToken ct = default) where TType : class
        {
            if (where == null)
            {
                return await dbSet.Select(select).ToListAsync(ct);
            }

            return await dbSet.Where(where).Select(select).ToListAsync(ct);
        }

        public async Task<TEntity?> GetByIdInclude(Expression<Func<TEntity, bool>>? filter = null, string includeProperties = "", CancellationToken ct = default)
        {
            IQueryable<TEntity> query = dbSet;

            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (!string.IsNullOrEmpty(includeProperties))
            {
                foreach (var includeProperty in includeProperties.Split
                (new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    query = query.Include(includeProperty.Trim());
                }
            }
            return await query.SingleOrDefaultAsync(ct);
        }

        public async Task<TEntity?> GetById(object id, CancellationToken ct = default)
        {
            return await dbSet.FindAsync(new[] { id }, ct);
        }

        public async Task<IEnumerable<TEntity>> GetWithRawSql(string query, params object[] parameters)
        {
            return await dbSet.FromSqlRaw(query, parameters).ToListAsync();
        }

        public async Task Insert(TEntity entity, CancellationToken ct = default)
        {
            await dbSet.AddAsync(entity, ct);
            await context.SaveChangesAsync(ct);
        }

        public async Task Update(TEntity entityToUpdate, CancellationToken ct = default)
        {
            dbSet.Update(entityToUpdate);
            await context.SaveChangesAsync(ct);
        }
    }
}
