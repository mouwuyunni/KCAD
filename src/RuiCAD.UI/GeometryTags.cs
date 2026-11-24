using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace RuiCAD.UI
{
    internal static class GeometryTags
    {
        private const string AppName = "KCAD_GEOMETRY";

        internal static void Set(Drafting drafting, Transaction transaction, ObjectId id, string role, string group, double value = 0)
        {
            var table = (RegAppTable)transaction.GetObject(drafting.Database.RegAppTableId, OpenMode.ForRead);
            if (!table.Has(AppName))
            {
                table.UpgradeOpen();
                using (var record = new RegAppTableRecord { Name = AppName })
                {
                    table.Add(record);
                    transaction.AddNewlyCreatedDBObject(record, true);
                }
            }

            var entity = (Entity)transaction.GetObject(id, OpenMode.ForWrite);
            using (var data = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, role),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, group),
                new TypedValue((int)DxfCode.ExtendedDataReal, value)))
                entity.XData = data;
        }

        internal static (string Role, string Group, double Value) Get(Entity entity)
        {
            using (var data = entity.GetXDataForApplication(AppName))
            {
                var values = data?.AsArray();
                if (values != null && values.Length == 4 && values[1].Value is string role &&
                    values[2].Value is string group && values[3].Value is double value)
                    return (role, group, value);
                return ("", "", 0);
            }
        }
    }
}
