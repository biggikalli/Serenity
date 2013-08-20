﻿namespace Serenity.Services
{
    using Serenity.Data;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Reflection;

    public class ListRequestHandler<TRow, TListRequest, TListResponse>
        where TRow: Row, new()
        where TListRequest: ListRequest
        where TListResponse: ListResponse<TRow>, new()
    {
        protected IDbConnection Connection;
        protected TRow Row;
        protected TListRequest Request;
        protected TListResponse Response;

        protected virtual SortBy[] GetNativeSort()
        {
            var nameRow = Row as INameRow;
            if (nameRow != null)
                return new SortBy[] { new SortBy(nameRow.NameField.Name, false) };

            return null;
        }

        protected virtual bool ShouldSelectField(Field field)
        {
            var mode = field.MinSelectLevel;
            
            if (mode == SelectLevel.Never ||
                (field.Flags & FieldFlags.ClientSide) == FieldFlags.ClientSide)
                return false;

            if (mode == SelectLevel.Always)
                return true;

            bool isPrimaryKey = (field.Flags & FieldFlags.PrimaryKey) == FieldFlags.PrimaryKey;
            if (isPrimaryKey && mode != SelectLevel.Explicit)
                return true;

            if (mode == SelectLevel.Default)
            {
                // assume that non-foreign calculated and reflective fields should be selected in list mode
                bool isForeign = (field.Flags & FieldFlags.Foreign) == FieldFlags.Foreign;
                mode = isForeign ? SelectLevel.Details : SelectLevel.List;
            }

            bool explicitlyExcluded = Request.ExcludeColumns != null &&
                (Request.ExcludeColumns.Contains(field.Name) ||
                    Request.ExcludeColumns.Contains(field.PropertyName));

            bool explicitlyIncluded = !explicitlyExcluded && Request.IncludeColumns != null &&
                (Request.IncludeColumns.Contains(field.Name) ||
                 Request.IncludeColumns.Contains(field.PropertyName));

            if (isPrimaryKey)
                return explicitlyIncluded;

            if (explicitlyExcluded)
                return false;

            if (explicitlyIncluded)
                return true;

            var selection = Request.ColumnSelection;

            switch (selection)
            {
                case ColumnSelection.Lookup:
                    return mode <= SelectLevel.Lookup;
                case ColumnSelection.List:
                    return mode <= SelectLevel.List;
                case ColumnSelection.Details:
                    return mode <= SelectLevel.Details;
                default:
                    return false;
            }
        }

        protected bool IsIncluded(Field field)
        {
            return Request.IncludeColumns != null &&
                (Request.IncludeColumns.Contains(field.Name) ||
                 (field.PropertyName != null && Request.IncludeColumns.Contains(field.PropertyName)));
        }

        protected bool IsIncluded(string column)
        {
            return Request.IncludeColumns != null &&
                Request.IncludeColumns.Contains(column);
        }

        protected virtual void SelectField(SqlSelect query, Field field)
        {
            query.Select(field);
        }

        protected virtual void SelectFields(SqlSelect query)
        {
            foreach (var field in Row.GetFields())
            {
                if (ShouldSelectField(field))
                    SelectField(query, field);
            }
        }
    
        protected virtual void PrepareQuery(SqlSelect query)
        {
            SelectFields(query);
        }

        protected virtual void ApplyKeyOrder(SqlSelect query)
        {
            var idRow = Row as IIdRow;
            if (idRow != null)
                query.OrderBy((Field)idRow.IdField);
        }

        protected virtual void ApplyContainsText(SqlSelect query, string containsText)
        {
            var nameRow = Row as INameRow;
            var idRow = Row as IIdRow;
            var idField = idRow != null ? (Field)idRow.IdField : null;
            var idFieldFilter = idField == null ? null : new Filter(idField);
            if (nameRow != null)
                query.ApplyContainsText(containsText, idFieldFilter, new Filter(0, nameRow.NameField));
            else if (idField != null)
                query.ApplyContainsText(containsText, idFieldFilter, new Filter[] { });
        }

        protected virtual void OnBeforeExecuteQuery()
        {
        }

        protected virtual void OnAfterExecuteQuery()
        {
        }

        protected virtual TRow ProcessEntity(TRow row)
        {
            return row;
        }

        protected virtual Filter ProcessCriteria(BasicCriteria criteria)
        {
            return null;
        }

        protected virtual FilterFields GetFilterFields()
        {
            return Row.GetFields().Filters;
        }

        protected virtual void ApplyFilters(SqlSelect query)
        {
            if (!Request.IncludeDeleted)
            {
                var isActiveRow = Row as IIsActiveRow;
                if (isActiveRow != null)
                    query.Where(new Filter(isActiveRow.IsActiveField) >= 0);
            }
        }

        protected virtual void ValidatePermissions()
        {
            var readAttr = typeof(TRow).GetCustomAttribute<ReadPermissionAttribute>(false);
            if (readAttr != null)
            {
                if (readAttr.ReadPermission.IsEmptyOrNull())
                    SecurityHelper.EnsureLoggedIn(RightErrorHandling.ThrowException);
                else
                    SecurityHelper.EnsurePermission(readAttr.ReadPermission, RightErrorHandling.ThrowException);
            }
        }

        protected virtual SqlSelect CreateQuery()
        {
            return new SqlSelect()
                .FromAs(Row, 0);
        }

        public TListResponse Process(IDbConnection connection, TListRequest request)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");

            Connection = connection;

            Request = request;
            Response = new TListResponse();
            Response.Entities = new List<TRow>();

            Row = new TRow();

            var query = CreateQuery();

            PrepareQuery(query);

            ApplyKeyOrder(query);

            query.ApplySkipTakeAndCount(request.Skip, request.Take, request.ExcludeTotalCount);

            ApplyContainsText(query, request.ContainsText);

            query.ApplySort(request.Sort, GetNativeSort());

            ApplyFilters(query);

            OnBeforeExecuteQuery();

            Response.TotalCount = query.ForEach(Connection, delegate()
            {
                var clone = ProcessEntity(Row.Clone());

                if (clone != null)
                    Response.Entities.Add(clone);
            });

            Response.SetSkipTakeTotal(query);

            OnAfterExecuteQuery();

            return Response;
        }
    }

    public class ListRequestHandler<TRow> : ListRequestHandler<TRow, ListRequest, ListResponse<TRow>>
        where TRow : Row, new()
    {
    }

    public class ListRequestHandler<TRow, TListRequest> : ListRequestHandler<TRow, TListRequest, ListResponse<TRow>>
        where TRow : Row, new()
        where TListRequest: ListRequest
    {
    }
}