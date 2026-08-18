using System.Linq.Expressions;
using My.DAL.Models.Paging;

namespace My.DAL.Repository
{
    public interface IRepository<TEntity> where TEntity : class
    {
        Task Delete(TEntity entityToDelete, CancellationToken ct = default);
        Task Delete(object id, CancellationToken ct = default);
        Task<TEntity?> Find(object id, CancellationToken ct = default);
        Task<PagedList<TEntity>> GetPaged(
            PagingParameters parameters,
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            string includeProperties = "",
            CancellationToken ct = default);
        Task<IEnumerable<TEntity>> Get(Expression<Func<TEntity, bool>>? filter = null, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, string includeProperties = "", CancellationToken ct = default);
        Task<IEnumerable<TType>> Get<TType>(Expression<Func<TEntity, TType>> select, Expression<Func<TEntity, bool>>? where = null, CancellationToken ct = default) where TType : class;
        Task<TEntity?> GetByIdInclude(Expression<Func<TEntity, bool>>? filter = null, string includeProperties = "", CancellationToken ct = default);
        Task<TEntity?> GetById(object id, CancellationToken ct = default);
        Task<IEnumerable<TEntity>> GetWithRawSql(string query, params object[] parameters);
        Task Insert(TEntity entity, CancellationToken ct = default);
        Task Update(TEntity entityToUpdate, CancellationToken ct = default);
    }
}
