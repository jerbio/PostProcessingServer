using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Web;
using TilerElements;
using TilerFront;

namespace PostProcessingServer.Services
{
    public class AnalysisLogControl:LogControlDirect
    {
        public AnalysisLogControl(TilerUser User, TilerDbContext dbContext, int cacheContextCount = 10)
            : base(User, dbContext, cacheContextCount)
        {
        }

        protected override List<Tuple<SqlParameter, string>> generateCalEventSqlParameters(CalendarEvent eachCalEvent, List<Tuple<SqlParameter, string>> loopQuery, string EvaluationId, int index, string calSqlPrefix, HashSet<DataRetrivalOption> allRetrievalOptions, bool allOptionsPersisted, List<CalendarEvent> calEventList, int calIndex, HashSet<string> relatedTilerIds)
        {
            string calSuffix = calSqlPrefix + index;
            loopQuery.Add(populateParams("IsSubEventSynced_DB", calSuffix, ((object)true) ?? DBNull.Value));
            return base.generateCalEventSqlParameters(eachCalEvent, loopQuery, EvaluationId, index, calSqlPrefix, allRetrievalOptions, allOptionsPersisted, calEventList, calIndex, relatedTilerIds);
        }
    }
}