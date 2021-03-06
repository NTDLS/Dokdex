﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dokdex.Library.Payloads;
using static Dokdex.Engine.Constants;
using Dokdex.Engine.Transactions;
using Dokdex.Engine.Schemas;
using Dokdex.Library;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Dokdex.Engine.Documents;
using Dokdex.Engine.Exceptions;
using System.Threading;
using Dokdex.Engine.Query;

namespace Dokdex.Engine.Indexes
{
    public class PersistIndexManager
    {
        private Core core;
        public PersistIndexManager(Core core)
        {
            this.core = core;
        }

        public IndexSelections SelectIndexes(Transaction transaction, PersistSchema schemaMeta, Conditions conditions)
        {
            try
            {

            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to select indexes for process {0}.", transaction.ProcessId), ex);
                throw;
            }

            IndexKeyMatches indexKeyMatches = new IndexKeyMatches(conditions);

            var indexCatalog = GetIndexCatalog(transaction, schemaMeta, LockOperation.Read);

            IndexSelections indexSelections = new IndexSelections();

            //Loop though each index in the schema.
            List<PotentialIndex> potentialIndexs = new List<PotentialIndex>();

            foreach (var indexMeta in indexCatalog.Collection)
            {
                List<string> handledKeyNames = new List<string>();

                for (int i = 0; i < indexMeta.Attributes.Count; i++)
                {
                    if (indexKeyMatches.Find(o => o.Key == indexMeta.Attributes[i].Name.ToLower() && o.Handled == false) != null)
                    {
                        handledKeyNames.Add(indexMeta.Attributes[i].Name.ToLower());
                    }
                    else
                    {
                        break;
                    }
                }

                if (handledKeyNames.Count > 0)
                {
                    potentialIndexs.Add(new PotentialIndex(indexMeta, handledKeyNames));
                }
            }

            //Grab the index that matches the most of our supplied keys but also has the least attributes.
            var firstIndex = (from o in potentialIndexs where o.Tried == false select o)
                .OrderByDescending(s => s.HandledKeyNames.Count)
                .ThenBy(t => t.Index.Attributes.Count).FirstOrDefault();
            if (firstIndex != null)
            {
                var handledKeys = (from o in indexKeyMatches where firstIndex.HandledKeyNames.Contains(o.Key) select o).ToList();
                foreach (var handledKey in handledKeys)
                {
                    handledKey.Handled = true;
                }

                firstIndex.Tried = true;

                indexSelections.Add(new IndexSelection(firstIndex.Index, firstIndex.HandledKeyNames));
            }

            indexSelections.UnhandledKeys.AddRange((from o in indexKeyMatches where o.Handled == false select o.Key).ToList());

            return indexSelections;
        }


        public bool Exists(UInt64 processId, string schema, string indexName)
        {
            bool result = false;
            try
            {
                using (var txRef = core.Transactions.Begin(processId))
                {
                    var schemaMeta = core.Schemas.VirtualPathToMeta(txRef.Transaction, schema, LockOperation.Read);
                    if (schemaMeta != null && schemaMeta.Exists)
                    {
                        var indexCatalog = GetIndexCatalog(txRef.Transaction, schemaMeta, LockOperation.Write);
                        if (indexCatalog != null)
                        {
                            result = indexCatalog.GetByName(indexName) != null;
                        }
                    }

                    txRef.Commit();
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to create index for process {0}.", processId), ex);
                throw;
            }

            return result;
        }

        public void Create(UInt64 processId, string schema, Index index, out Guid newId)
        {
            try
            {
                var persistIndex = PersistIndex.FromPayload(index);

                if (persistIndex.Id == Guid.Empty)
                {
                    persistIndex.Id = Guid.NewGuid();
                }
                if (persistIndex.Created == DateTime.MinValue)
                {
                    persistIndex.Created = DateTime.UtcNow;
                }
                if (persistIndex.Modfied == DateTime.MinValue)
                {
                    persistIndex.Modfied = DateTime.UtcNow;
                }

                using (var txRef = core.Transactions.Begin(processId))
                {
                    var schemaMeta = core.Schemas.VirtualPathToMeta(txRef.Transaction, schema, LockOperation.Read);
                    if (schemaMeta == null || schemaMeta.Exists == false)
                    {
                        throw new DokdexSchemaDoesNotExistException(schema);
                    }

                    var indexCatalog = GetIndexCatalog(txRef.Transaction, schemaMeta, LockOperation.Write);
                    indexCatalog.Add(persistIndex);
                    core.IO.PutJson(txRef.Transaction, indexCatalog.DiskPath, indexCatalog);

                    persistIndex.DiskPath = Path.Combine(schemaMeta.DiskPath, MakeIndexFileName(index.Name));
                    core.IO.PutPBuf(txRef.Transaction, persistIndex.DiskPath, new PersistIndexPageCatalog());

                    RebuildIndex(txRef.Transaction, schemaMeta, persistIndex);

                    newId = persistIndex.Id;

                    txRef.Commit();
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to create index for process {0}.", processId), ex);
                throw;
            }
        }

        public void Rebuild(UInt64 processId, string schema, string indexName)
        {
            try
            {           
                using (var txRef = core.Transactions.Begin(processId))
                {
                    var schemaMeta = core.Schemas.VirtualPathToMeta(txRef.Transaction, schema, LockOperation.Read);
                    if (schemaMeta == null || schemaMeta.Exists == false)
                    {
                        throw new DokdexSchemaDoesNotExistException(schema);
                    }

                    var indexCatalog = GetIndexCatalog(txRef.Transaction, schemaMeta, LockOperation.Write);

                    var indexMeta = indexCatalog.GetByName(indexName);
                    if (indexMeta == null)
                    {
                        throw new DokdexIndexDoesNotExistException(schema);
                    }

                    indexMeta.DiskPath = Path.Combine(schemaMeta.DiskPath, MakeIndexFileName(indexMeta.Name));

                    RebuildIndex(txRef.Transaction, schemaMeta, indexMeta);
   
                    txRef.Commit();
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to rebuild index for process {0}.", processId), ex);
                throw;
            }
        }

        private PersistIndexCatalog GetIndexCatalog(Transaction transaction, string schema, LockOperation intendedOperation)
        {
            var schemaMeta = core.Schemas.VirtualPathToMeta(transaction, schema, intendedOperation);
            return GetIndexCatalog(transaction, schemaMeta, intendedOperation);
        }

        public string MakeIndexFileName(string indexName)
        {
            return string.Format("@Idx_{0}_Pages.PBuf", Helpers.MakeSafeFileName(indexName));
        }

        private PersistIndexCatalog GetIndexCatalog(Transaction transaction, PersistSchema schemaMeta, LockOperation intendedOperation)
        {
            string indexCatalogDiskPath = Path.Combine(schemaMeta.DiskPath, Constants.IndexCatalogFile);
            var indexCatalog = core.IO.GetJson<PersistIndexCatalog>(transaction, indexCatalogDiskPath, intendedOperation);
            indexCatalog.DiskPath = indexCatalogDiskPath;

            foreach (var index in indexCatalog.Collection)
            {
                index.DiskPath = Path.Combine(schemaMeta.DiskPath, MakeIndexFileName(index.Name));
            }

            return indexCatalog;
        }

        private List<string> GetIndexSearchTokens(Transaction transaction, PersistIndex indexMeta, PersistDocument document)
        {
            try
            {
                List<string> result = new List<string>();

                foreach (var indexAttribute in indexMeta.Attributes)
                {
                    var jsonContent = JObject.Parse(document.Content);
                    JToken jToken = null;
                    if (jsonContent.TryGetValue(indexAttribute.Name, StringComparison.CurrentCultureIgnoreCase, out jToken))
                    {
                        result.Add(jToken.ToString());
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to build index search tokens for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        /// <summary>
        // Finds the appropriate index page for a set of key values.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="indexMeta"></param>
        /// <param name="searchTokens"></param>
        /// <returns></returns>
        private FindKeyPageResult FindKeyPage(Transaction transaction, PersistIndex indexMeta, List<string> searchTokens)
        {
            return FindKeyPage(transaction, indexMeta, searchTokens, null);
        }

        /// <summary>
        /// Finds the appropriate index page for a set of key values using a long lived index page catalog.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="indexMeta"></param>
        /// <param name="searchTokens"></param>
        /// <param name="indexPageCatalog"></param>
        /// <returns></returns>
        private FindKeyPageResult FindKeyPage(Transaction transaction, PersistIndex indexMeta, List<string> searchTokens, PersistIndexPageCatalog suppliedIndexPageCatalog)
        {
            try
            {
                var indexPageCatalog = suppliedIndexPageCatalog;
                if (indexPageCatalog == null)
                {
                    indexPageCatalog = core.IO.GetPBuf<PersistIndexPageCatalog>(transaction, indexMeta.DiskPath, LockOperation.Write);
                }

                lock (indexPageCatalog)
                {
                    FindKeyPageResult result = new FindKeyPageResult()
                    {
                        Catalog = indexPageCatalog
                    };

                    result.Leaves = result.Catalog.Leaves;
                    if (result.Leaves == null || result.Leaves.Count == 0)
                    {
                        //The index is empty.
                        return result;
                    }

                    int foundExtentCount = 0;

                    foreach (var token in searchTokens)
                    {
                        bool locatedExtent = false;

                        foreach (var leaf in result.Leaves)
                        {
                            if (leaf.Key == token)
                            {
                                locatedExtent = true;
                                foundExtentCount++;
                                result.Leaf = leaf;
                                result.Leaves = leaf.Leaves; //Move one level lower in the extent tree.

                                result.IsPartialMatch = true;
                                result.ExtentLevel = foundExtentCount;

                                if (foundExtentCount == searchTokens.Count)
                                {
                                    result.IsPartialMatch = false;
                                    result.IsFullMatch = true;
                                    return result;
                                }
                            }
                        }

                        if (locatedExtent == false)
                        {
                            return result;
                        }
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to locate key page for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        /// <summary>
        /// Updates an index entry for a single document into each index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schema"></param>
        /// <param name="document"></param>
        public void UpdateDocumentIntoIndexes(Transaction transaction, PersistSchema schemaMeta, PersistDocument document)
        {
            try
            {
                var indexCatalog = GetIndexCatalog(transaction, schemaMeta, LockOperation.Read);

                //Loop though each index in the schema.
                foreach (var indexMeta in indexCatalog.Collection)
                {
                    DeleteDocumentFromIndex(transaction, schemaMeta, indexMeta, document.Id);
                    InsertDocumentIntoIndex(transaction, schemaMeta, indexMeta, document);
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Multi-index insert failed for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        /// <summary>
        /// Inserts an index entry for a single document into each index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schema"></param>
        /// <param name="document"></param>
        public void InsertDocumentIntoIndexes(Transaction transaction, PersistSchema schemaMeta, PersistDocument document)
        {
            try
            {
                var indexCatalog = GetIndexCatalog(transaction, schemaMeta, LockOperation.Read);

                //Loop though each index in the schema.
                foreach (var indexMeta in indexCatalog.Collection)
                {
                    InsertDocumentIntoIndex(transaction, schemaMeta, indexMeta, document);
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Multi-index insert failed for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        /// <summary>
        /// Inserts an index entry for a single document into a single index.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schemaMeta"></param>
        /// <param name="indexMeta"></param>
        /// <param name="document"></param>
        private void InsertDocumentIntoIndex(Transaction transaction, PersistSchema schemaMeta, PersistIndex indexMeta, PersistDocument document)
        {
            InsertDocumentIntoIndex(transaction, schemaMeta, indexMeta, document, null, true);
        }

        /// <summary>
        /// Inserts an index entry for a single document into a single index using a long lived index page catalog.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schemaMeta"></param>
        /// <param name="indexMeta"></param>
        /// <param name="document"></param>
        private void InsertDocumentIntoIndex(Transaction transaction, PersistSchema schemaMeta, PersistIndex indexMeta, PersistDocument document, PersistIndexPageCatalog indexPageCatalog, bool flushPageCatalog)
        {
            try
            {
                var searchTokens = GetIndexSearchTokens(transaction, indexMeta, document);
                var findResult = FindKeyPage(transaction, indexMeta, searchTokens, indexPageCatalog);

                //If we found a full match for all supplied key values - add the document to the leaf collection.
                if (findResult.IsFullMatch)
                {
                    if (findResult.Leaf.DocumentIDs == null)
                    {
                        findResult.Leaf.DocumentIDs = new HashSet<Guid>();
                    }

                    if (indexMeta.IsUnique && findResult.Leaf.DocumentIDs.Count > 1)
                    {
                        string exceptionText = string.Format("Duplicate key violation occurred for index [{0}]/[{1}]. Values: {{{2}}}",
                            schemaMeta.VirtualPath, indexMeta.Name, string.Join(",", searchTokens));

                        throw new DokdexDuplicateKeyViolation(exceptionText);
                    }

                    findResult.Leaf.DocumentIDs.Add(document.Id);
                    if (flushPageCatalog)
                    {
                        core.IO.PutPBuf(transaction, indexMeta.DiskPath, findResult.Catalog);
                    }
                }
                else
                {
                    //If we didn't find a full match for all supplied key values,
                    //  then create the tree and add the document to the lowest leaf.
                    //Note that we are going to start creating the leaf level at the findResult.ExtentLevel.
                    //  This is because we may have a partial match and don't need to create the full tree.
                    lock (indexPageCatalog)
                    {
                        for (int i = findResult.ExtentLevel; i < searchTokens.Count; i++)
                        {
                            findResult.Leaf = findResult.Leaves.AddNewleaf(searchTokens[i]);
                            findResult.Leaves = findResult.Leaf.Leaves;
                        }

                        if (findResult.Leaf.DocumentIDs == null)
                        {
                            findResult.Leaf.DocumentIDs = new HashSet<Guid>();
                        }

                        findResult.Leaf.DocumentIDs.Add(document.Id);
                    }
                    if (flushPageCatalog)
                    {
                        core.IO.PutPBuf(transaction, indexMeta.DiskPath, findResult.Catalog);
                    }
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Index document insert failed for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        class RebuildIndexItemThreadProc_ParallelState
        {
            public int ThreadsCompleted { get; set; }
            public int ThreadsStarted { get; set; }
            public int TargetThreadCount { get; set; }

            public bool IsComplete
            {
                get
                {
                    lock (this)
                    {
                        return (ThreadsStarted - ThreadsCompleted) == 0;
                    }
                }
            }
        }

        class RebuildIndexItemThreadProc_Params
        {
            public RebuildIndexItemThreadProc_ParallelState State { get; set; }
            public Transaction Transaction { get; set; }
            public PersistSchema SchemaMeta { get; set; }
            public PersistIndex IndexMeta { get; set; }
            public PersistDocumentCatalog DocumentCatalog { get; set; }
            public PersistIndexPageCatalog IndexPageCatalog { get; set; }
            public AutoResetEvent Initialized { get; set; }
            public RebuildIndexItemThreadProc_Params()
            {
                Initialized = new AutoResetEvent(false);
            }
        }

        void RebuildIndexItemThreadProc(object oParam)
        {
            RebuildIndexItemThreadProc_Params param = (RebuildIndexItemThreadProc_Params)oParam;

            int threadMod = 0;

            lock (param.State)
            {
                threadMod = param.State.ThreadsStarted;
                param.State.ThreadsStarted++;
                Thread.CurrentThread.Name = "RebuildIndexItemThreadProc_" + param.State.ThreadsStarted;
                param.Initialized.Set();
            }

            for (int i = 0; i < param.DocumentCatalog.Collection.Count; i++)
            {
                if ((i % param.State.TargetThreadCount) == threadMod)
                {
                    var documentCatalogItem = param.DocumentCatalog.Collection[i];
                    string documentDiskPath = Path.Combine(param.SchemaMeta.DiskPath, documentCatalogItem.FileName);
                    var persistDocument = core.IO.GetJson<PersistDocument>(param.Transaction, documentDiskPath, LockOperation.Read);
                    InsertDocumentIntoIndex(param.Transaction, param.SchemaMeta, param.IndexMeta, persistDocument, param.IndexPageCatalog, false);
                }
            }

            lock (param.State)
            {
                param.State.ThreadsCompleted++;
            }
        }

        /// <summary>
        /// Inserts all documents in a schema into a single index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schemaMeta"></param>
        /// <param name="indexMeta"></param>
        private void RebuildIndex(Transaction transaction,  PersistSchema schemaMeta, PersistIndex indexMeta)
        {
            try
            {
                var filePath = Path.Combine(schemaMeta.DiskPath, Constants.DocumentCatalogFile);
                var documentCatalog = core.IO.GetJson<PersistDocumentCatalog>(transaction, filePath, LockOperation.Read);

                //Clear out the existing index pages.
                core.IO.PutPBuf(transaction, indexMeta.DiskPath, new PersistIndexPageCatalog());

                var indexPageCatalog = core.IO.GetPBuf<PersistIndexPageCatalog>(transaction, indexMeta.DiskPath, LockOperation.Write);

                var state = new RebuildIndexItemThreadProc_ParallelState()
                {
                    TargetThreadCount = Environment.ProcessorCount * 2
                };

                var param = new RebuildIndexItemThreadProc_Params()
                {
                    DocumentCatalog = documentCatalog,
                    State = state,
                    IndexMeta = indexMeta,
                    IndexPageCatalog = indexPageCatalog,
                    SchemaMeta = schemaMeta,
                    Transaction = transaction
                };

                for (int i = 0; i < state.TargetThreadCount; i++)
                {
                    new Thread(RebuildIndexItemThreadProc).Start(param);
                    param.Initialized.WaitOne(Timeout.Infinite);
                }

                while (state.IsComplete == false)
                {
                    Thread.Sleep(1);
                }

                core.IO.PutPBuf(transaction, indexMeta.DiskPath, indexPageCatalog);
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Failed to rebuild single index for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        public void DeleteDocumentFromIndexes(Transaction transaction, PersistSchema schemaMeta, Guid documentId)
        {
            try
            {
                var indexCatalog = GetIndexCatalog(transaction, schemaMeta, LockOperation.Read);

                //Loop though each index in the schema.
                foreach (var indexMeta in indexCatalog.Collection)
                {
                    DeleteDocumentFromIndex(transaction, schemaMeta, indexMeta, documentId);
                }
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Multi-index upsert failed for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

        private bool RemoveDocumentFromLeaves(ref PersistIndexLeaves leaves, Guid documentId)
        {
            foreach (var leaf in leaves)
            {
                if (leaf.DocumentIDs != null && leaf.DocumentIDs.Count > 0)
                {
                    if (leaf.DocumentIDs.Remove(documentId))
                    {
                        return true; //We found the document and removed it.
                    }
                }

                if (leaf.Leaves != null && leaf.Leaves.Count > 0)
                {
                    if (RemoveDocumentFromLeaves(ref leaf.Leaves, documentId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Removes a document from an index.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schemaMeta"></param>
        /// <param name="indexMeta"></param>
        /// <param name="document"></param>
        private void DeleteDocumentFromIndex(Transaction transaction, PersistSchema schemaMeta, PersistIndex indexMeta, Guid documentId)
        {
            try
            {
                var persistIndexPageCatalog = core.IO.GetPBuf<PersistIndexPageCatalog>(transaction, indexMeta.DiskPath, LockOperation.Write);

                if (RemoveDocumentFromLeaves(ref persistIndexPageCatalog.Leaves, documentId))
                {
                    core.IO.PutPBuf(transaction, indexMeta.DiskPath, persistIndexPageCatalog);
                }                
            }
            catch (Exception ex)
            {
                core.Log.Write(String.Format("Index document upsert failed for process {0}.", transaction.ProcessId), ex);
                throw;
            }
        }

    }
}
