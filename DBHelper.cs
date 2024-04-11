using SanTint.Common.Log;
using SanTint.Opc.Server.Model;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanTint.Opc.Server
{
    public class DBHelper
    {
        private readonly static string _dbPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "DataBase", "opc.db");

        /// <summary>
        /// 检测SQLite数据库是否存在,不存在则创建
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public static void CheckOrInitSqliteDb()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (File.Exists(_dbPath)) return;

            try
            {
                Logger.Write("Sqlite数据库不存在,重新创建数据库, category: Common.Utility.CategoryLog.Error");
                //创建数据库
                var db = new SQLite.SQLiteConnection(_dbPath);
                db.CreateTable<ADUSent>();
                db.CreateTable<ADUReceived>();
                db.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Write("创建Sqlite Db失败", category: Common.Utility.CategoryLog.Error);
                Logger.Write(ex, category: Common.Utility.CategoryLog.Error);
                throw ex;
            }
        }

        #region ADUSent

        public List<ADUSent> QueryADUSentByCode(string ProcessOrderOrMaterialCode)
        {
            var result = new List<ADUSent>();
            var db = new SQLite.SQLiteConnection(_dbPath);
            result = db.Query<ADUSent>($"select * from ADUSent where (ProcessOrder like '%{ProcessOrderOrMaterialCode}%'  or MaterialCode like  '%{ProcessOrderOrMaterialCode}%') and IFNULL(IsSTComplete, 0) = 0", ProcessOrderOrMaterialCode);
            //result = db.Query<ADUSent>("select * from ADUSent where ProcessOrder like '%@ProcessOrderOrMaterialCode%' or MaterialCode like '%@ProcessOrderOrMaterialCode%'", ProcessOrderOrMaterialCode);
            //经测试无法使用参数形式,用于like查询
            return result;
        }

        public List<ADUSent> QueryADUSent(ADUSent aduSent)
        {
            var result = new List<ADUSent>();
            var db = new SQLite.SQLiteConnection(_dbPath);
            var tb = db.Table<ADUSent>();
            if (!string.IsNullOrWhiteSpace(aduSent.ProcessOrder))
            {
                tb = tb.Where(x => x.ProcessOrder == aduSent.ProcessOrder);
            }
            if (!string.IsNullOrWhiteSpace(aduSent.DeviceIdentifier))
            {
                tb = tb.Where(x => x.DeviceIdentifier == aduSent.DeviceIdentifier);
            }
            if (!string.IsNullOrWhiteSpace(aduSent.DataIdentifier))
            {
                tb = tb.Where(x => x.DataIdentifier == aduSent.DataIdentifier);
            }
            if (!string.IsNullOrWhiteSpace(aduSent.MaterialCode))
            {
                tb = tb.Where(x => x.MaterialCode == aduSent.MaterialCode);
            }
            result = tb.ToList();
            db.Dispose();
            return result;
        }

        public bool IsDataIdentifierExistForADUSent(string DataIdentifier)
        {
            var db = new SQLite.SQLiteConnection(_dbPath);
            var result = db.Query<ADUReceived>($"select * from ADUSent where DataIdentifier = '{DataIdentifier}'");
            db.Dispose();
            return result.Any();
        }

        public bool AddADUSent(ADUSent aduSent)
        {
            var db = new SQLite.SQLiteConnection(_dbPath);
            var result = db.Insert(aduSent);
            db.Dispose();
            return result > 0;
        }

        public bool UpdateADUSent(ADUSent aduSent)
        {
            var db = new SQLite.SQLiteConnection(_dbPath);
            var result = db.Update(aduSent);
            db.Dispose();
            return result > 0;
        }
        #endregion

        #region ADUReceived
        public bool AddADUReceived(ADUReceived aduReceived)
        {


            ADUSent queryADUSent = new ADUSent() { DataIdentifier = aduReceived.DataIdentifier };
            //更新ADUSent,三华已经完成
            var re = QueryADUSent(queryADUSent);
            if (re.Any()) re.ForEach(t =>
            {
                t.IsSTComplete = true;
                UpdateADUSent(t);
            });
            aduReceived.ID=0;
            aduReceived.IsComplete= false;
            var db = new SQLite.SQLiteConnection(_dbPath);
            var result = db.Insert(aduReceived);
            db.Dispose();
            return result > 0;
        }

        public bool IsDataIdentifierExistForADUReceived(string DataIdentifier)
        {
            var db = new SQLite.SQLiteConnection(_dbPath);
            var result = db.Query<ADUReceived>($"select * from ADUReceived where DataIdentifier = '{DataIdentifier}'");
            db.Dispose();
            return result.Any();
        }

        public bool UpdateADUReceived(ADUReceived aduReceived)
        {
            var db = new SQLite.SQLiteConnection(_dbPath);
            var result = db.Update(aduReceived);
            db.Dispose();
            return result > 0;
        }

        public List<ADUReceived> GetUncompleteADUReceived()
        {
            var result = new List<ADUReceived>();
            var db = new SQLite.SQLiteConnection(_dbPath);
            result = db.Query<ADUReceived>($"select * from ADUReceived where  IFNULL(IsComplete,0) = 0  ORDER BY  ID Desc LIMIT 1");
            //result = db.Query<ADUSent>("select * from ADUSent where ProcessOrder like '%@ProcessOrderOrMaterialCode%' or MaterialCode like '%@ProcessOrderOrMaterialCode%'", ProcessOrderOrMaterialCode);
            //经测试无法使用参数形式,用于like查询
            return result;
        }

        #endregion

    }
}
