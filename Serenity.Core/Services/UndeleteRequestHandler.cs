﻿using Serenity.Data;
using System;
using System.Data;
using System.Reflection;

namespace Serenity.Services
{
    public class UndeleteRequestHandler<TRow, TUndeleteResponse>
        where TRow : Row, IIdRow, new()
        where TUndeleteResponse : UndeleteResponse, new()
    {
        protected IUnitOfWork UnitOfWork;
        protected TRow Row;
        protected TUndeleteResponse Response;
        protected UndeleteRequest Request;
        private static bool loggingInitialized;
        protected static CaptureLogHandler<TRow> captureLogHandler;

        protected IDbConnection Connection
        {
            get { return UnitOfWork.Connection; }
        }

        protected virtual AuditUndeleteRequest GetAuditRequest()
        {
            //EntityType entityType;
            //if (SiteSchema.Instance.TableToType.TryGetValue(Row.Table, out entityType))
            {
                var auditRequest = new AuditUndeleteRequest(Row.Table, Row.IdField[Row].Value);

                var parentIdRow = Row as IParentIdRow;
                if (parentIdRow != null)
                {
                    var parentIdField = (Field)parentIdRow.ParentIdField;
                    //EntityType parentEntityType;
                    if (!parentIdField.ForeignTable.IsEmptyOrNull())// &&
                        //SiteSchema.Instance.TableToType.TryGetValue(parentIdField.ForeignTable, out parentEntityType))
                    {
                        auditRequest.ParentTypeId = parentIdField.ForeignTable;
                        auditRequest.ParentId = parentIdRow.ParentIdField[Row];
                    }
                }

                return auditRequest;
            }
        }

        protected virtual void OnBeforeUndelete()
        {
        }

        protected virtual void OnAfterUndelete()
        {
        }

        protected virtual void ValidateRequest()
        {
        }

        protected virtual void DoGenericAudit()
        {
            var auditRequest = GetAuditRequest();
            if (auditRequest != null)
                AuditLogService.AuditUndelete(Connection, RowRegistry.GetSchemaName(Row), auditRequest);
        }

        protected virtual void DoCaptureLog()
        {
            ((IIsActiveRow)Row).IsActiveField[Row] = 1;
            captureLogHandler.Log(this.UnitOfWork, this.Row, SecurityHelper.CurrentUserId, isDelete: false);
        }

        protected virtual void DoAudit()
        {
            if (!loggingInitialized)
            {
                var logTableAttr = Row.GetType().GetCustomAttribute<CaptureLogAttribute>();
                if (logTableAttr != null)
                    captureLogHandler = new CaptureLogHandler<TRow>();

                loggingInitialized = true;
            }

            if (captureLogHandler != null)
            {
                DoCaptureLog();
            }
            else
                DoGenericAudit();
        }

        protected virtual void PrepareQuery(SqlSelect query)
        {
            query.SelectTableFields();
        }

        protected virtual void LoadEntity()
        {
            var idField = (Field)Row.IdField;

            var query = new SqlSelect().FromAs(Row, 0)
                .WhereEqual(idField, Request.EntityId.Value);

            PrepareQuery(query);

            if (!query.GetFirst(Connection))
                throw DataValidation.EntityNotFoundError(Row, Request.EntityId.Value);
        }

        protected virtual void OnReturn()
        {
        }

        protected virtual void ValidatePermissions()
        {
            var modifyAttr = typeof(TRow).GetCustomAttribute<ModifyPermissionAttribute>(false);
            if (modifyAttr != null)
            {
                if (modifyAttr.ModifyPermission.IsEmptyOrNull())
                    SecurityHelper.EnsureLoggedIn(RightErrorHandling.ThrowException);
                else
                    SecurityHelper.EnsurePermission(modifyAttr.ModifyPermission, RightErrorHandling.ThrowException);
            }
        }

        protected virtual void InvalidateCacheOnCommit()
        {
            var attr = typeof(TRow).GetCustomAttribute<TwoLevelCachedAttribute>(false);
            if (attr != null)
            {
                BatchGenerationUpdater.OnCommit(this.UnitOfWork, Row.GetFields().GenerationKey);
                foreach (var key in attr.GenerationKeys)
                    BatchGenerationUpdater.OnCommit(this.UnitOfWork, key);
            }
        }

        public TUndeleteResponse Process(IUnitOfWork unitOfWork, UndeleteRequest request)
        {
            if (unitOfWork == null)
                throw new ArgumentNullException("unitOfWork");

            ValidatePermissions();

            UnitOfWork = unitOfWork;

            Request = request;
            Response = new TUndeleteResponse();

            if (request.EntityId == null)
                throw DataValidation.RequiredError("EntityId");

            Row = new TRow();

            var isActiveRow = Row as IIsActiveRow;

            if (isActiveRow == null)
                throw new NotImplementedException();

            var idField = (Field)Row.IdField;

            LoadEntity();

            ValidateRequest();

            if (isActiveRow.IsActiveField[Row] > 0)
                Response.WasNotDeleted = true;
            else
            {
                OnBeforeUndelete();

                if (new SqlUpdate(Row.Table)
                        .Set(isActiveRow.IsActiveField, 1)
                        .WhereEqual(idField, request.EntityId.Value)
                        .WhereEqual(isActiveRow.IsActiveField, -1)
                        .Execute(Connection) != 1)
                    throw DataValidation.EntityNotFoundError(Row, request.EntityId.Value);

                InvalidateCacheOnCommit();

                OnAfterUndelete();

                DoAudit();
            }

            OnReturn();

            return Response;
        }
    }

    public class UndeleteRequestHandler<TRow> : UndeleteRequestHandler<TRow, UndeleteResponse>
        where TRow : Row, IIdRow, new()
    {
    }
}