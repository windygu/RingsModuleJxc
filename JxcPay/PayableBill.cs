﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Rings.Models;
using Jxc.Utility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace JxcPay
{
    public class PayableBill : MarshalByRefObject
    {
        private string billname = "payablebill";
        public Object Init(string parameters)
        {
            string billcode = BillHelper.GetBillCode(billname);
            JObject content = new JObject();
            content.Add("billcode", billcode);
            content.Add("billname", billname);
            content.Add("billdate", DateTime.Now.ToString("yyyy-MM-dd"));

            DBHelper db = new DBHelper();

            var option = db.First("select * from option");
            int digit = 2;
            bool noeditbillcode = option.content["noeditbillcode"] == null ? false : option.content.Value<bool>("noeditbillcode");
            bool noeditbilldate = option.content["noeditbilldate"] == null ? false : option.content.Value<bool>("noeditbilldate");

            return new
            {
                content = content,
                makername = PluginContext.Current.Account.Name,
                digit = digit,
                noeditbillcode = noeditbillcode,
                noeditbilldate = noeditbilldate
            };
        }

        public Object GetCheckoutBills(string parameters)
        {
            ParameterHelper ph = new ParameterHelper(parameters);
            int vendorid = ph.GetParameterValue<int>("vendorid");

            DBHelper db = new DBHelper();
            string sql = "select * from bill where content->>'billname' in ('purchasebill','purchasebackbill','feebill') " +
                " and (content->>'vendorid')::int=" + vendorid + " and (content->>'auditstatus')::int=1 " +
                " and abs(coalesce(content->>'checkouttotal','0')::decimal)<(content->>'total')::decimal";
             
            var bills = db.Where(sql);
            foreach (var item in bills)
            {
                item.content.Add("makername", db.First("employee", item.content.Value<int>("makerid")).content.Value<string>("name"));
                item.content.Add("employeename", db.First("employee", item.content.Value<int>("employeeid")).content.Value<string>("name"));
                string billname = item.content.Value<string>("billname");
                if (billname == "purchasebackbill")
                {
                    item.content["total"] = -item.content.Value<decimal>("total");
                }
            }

            decimal totalsum = bills.Sum(c => c.content.Value<decimal>("total"));
            decimal checkouttotalsum = bills.Sum(c => c.content.Value<decimal>("checkouttotal"));

            return new
            { 
                totalsum=totalsum,
                checkouttotalsum=checkouttotalsum,
                data = bills,
            };
        }

        public Object GetCheckoutBillsHistory(string parameters)
        {
            ParameterHelper ph = new ParameterHelper(parameters);
            int billid = ph.GetParameterValue<int>("billid");
             
            DBHelper db = new DBHelper();

            var bill = db.First("bill",billid);
            StringBuilder sb = new StringBuilder();
            Dictionary<int, decimal> dic = new Dictionary<int, decimal>();
            foreach (var item in bill.content.Value<JArray>("checkoutbills").Values<JObject>())
            {
                sb.Append(item.Value<int>("billid"));
                sb.Append(",");
                dic.Add(item.Value<int>("billid"), item.Value<decimal>("checkouttotal"));
            }

            string where = sb.ToString().Substring(0, sb.Length - 1);

            string sql = "select * from bill where id in ("+where+")";
 
            var bills = db.Where(sql);
            foreach (var item in bills)
            {
                item.content.Add("makername", db.First("employee", item.content.Value<int>("makerid")).content.Value<string>("name"));
                item.content.Add("employeename", db.First("employee", item.content.Value<int>("employeeid")).content.Value<string>("name"));
                string billname = item.content.Value<string>("billname");
                if (billname == "purchasebackbill")
                {
                    item.content["total"] = -item.content.Value<decimal>("total");
                }
                item.content.Add("thischeckouttotal", dic[item.id]);
            }

            decimal totalsum = bills.Sum(c => c.content.Value<decimal>("total"));
            decimal checkouttotalsum = bills.Sum(c => c.content.Value<decimal>("checkouttotal"));

            return new
            {
                totalsum = totalsum,
                checkouttotalsum = checkouttotalsum,
                data = bills,
            };
        }


        public Object PayableBillQuery(string parameters)
        {
            QueryParameter para = ParameterHelper.GetQueryParameters(parameters);

            var myfilter = JsonConvert.DeserializeObject<Dictionary<string, string>>(para.filter);

            int pageindex = para.page ?? 1;
            int pagesize = para.count ?? 25;

            DBHelper db = new DBHelper();
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(" where content->>'billname'='{0}'", billname);

            foreach (string field in myfilter.Keys)
            {
                if (string.IsNullOrEmpty(myfilter[field]))
                    continue;

                string f = myfilter[field].Trim();
                if (string.IsNullOrEmpty(f))
                    continue;

                if (field == "startdate")
                {
                    sb.AppendFormat(" and content->>'billdate'>='{0}'", f);
                }
                else if (field == "enddate")
                {
                    sb.AppendFormat(" and content->>'billdate'<='{0}'", f);
                }
                else if (field == "billcode")
                {
                    sb.AppendFormat(" and content->>'billcode' ilike '%{0}%'", f);
                }
                else if (field == "vendorname")
                {
                    DataTable dt = db.QueryTable("select id from vendor where content->>'name' ilike '%" + f + "%' or content->>'code' ilike '%" + f + "%'");
                    string ids = dt.GetIds();
                    sb.AppendFormat(" and (content->>'vendorid')::int in ({0})", ids);
                }
                else if (field == "employeename")
                {
                    DataTable dt = db.QueryTable("select id from employee where content->>'name' ilike '%" + f + "%' or content->>'code' ilike '%" + f + "%'");
                    string ids = dt.GetIds();
                    sb.AppendFormat(" and (content->>'employeeid')::int in ({0})", ids);
                }
                else if (field == "makername")
                {
                    DataTable dt = db.QueryTable("select id from employee where content->>'name' ilike '%" + f + "%' or content->>'code' ilike '%" + f + "%'");
                    string ids = dt.GetIds();
                    sb.AppendFormat(" and (content->>'makerid')::int in ({0})", ids);
                }
                else if (field == "comment")
                {
                    sb.AppendFormat(" and content->>'comment' ilike '%{0}%'", f);
                }
                else if (field == "status")
                {
                    if (f == "draft") sb.Append(" and (content->>'auditstatus')::int=0");
                    else if (f == "audited") sb.Append(" and (content->>'auditstatus')::int=1");
                }

            }

            StringBuilder sborder = new StringBuilder();
            sborder.Append(" order by content->>'billdate' desc,id desc ");


            sborder.AppendFormat(" limit {0} offset {1} ", pagesize, pagesize * pageindex - pagesize);

            string sql = "select *  from bill "
                        + sb.ToString()
                        + sborder.ToString();

            string sqlCount = "select count(0) as cnt from bill "
                        + sb.ToString();


            string sqlTotalSum = "select coalesce(sum(total),0) as total " +
                                "from  " +
                                "( " +
                                "select  ((jsonb_array_elements(content->'details')->>'total')::decimal) as total from bill " + sb.ToString() +
                                ") as t";

            int recordcount = db.Count(sqlCount);
            decimal totalsum = Convert.ToDecimal(db.Scalar(sqlTotalSum));

            List<TableModel> list = db.Where(sql);
            foreach (var item in list)
            {
                item.content.Add("makername", db.First("employee", item.content.Value<int>("makerid")).content.Value<string>("name"));
                item.content.Add("vendorname", db.First("vendor", item.content.Value<int>("vendorid")).content.Value<string>("name"));
                item.content.Add("employeename", db.First("employee", item.content.Value<int>("employeeid")).content.Value<string>("name"));
                
            }

            return new
            {
                resulttotal = recordcount,
                totalsum = totalsum,
                data = list,
            };
        }


        public Object LoadBillQuery(string parameters)
        {
            QueryParameter para = ParameterHelper.GetQueryParameters(parameters);

            var myfilter = JsonConvert.DeserializeObject<Dictionary<string, string>>(para.filter);

            int pageindex = para.page ?? 1;
            int pagesize = para.count ?? 25;

            DBHelper db = new DBHelper();
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(" where content->>'billname'='{0}'", billname);

            foreach (string field in myfilter.Keys)
            {
                if (string.IsNullOrEmpty(myfilter[field]))
                    continue;

                string f = myfilter[field].Trim();
                if (string.IsNullOrEmpty(f))
                    continue;

                if (field == "startdate")
                {
                    sb.AppendFormat(" and content->>'billdate'>='{0}'", f);
                }
                else if (field == "enddate")
                {
                    sb.AppendFormat(" and content->>'billdate'<='{0}'", f);
                }
                else if (field == "billcode")
                {
                    sb.AppendFormat(" and content->>'billcode' ilike '%{0}%'", f);
                }
                else if (field == "vendorname")
                {
                    DataTable dt = db.QueryTable("select id from vendor where content->>'name' ilike '%" + f + "%'");
                    string ids = dt.GetIds();
                    sb.AppendFormat(" and (content->>'vendorid')::int in ({0})", ids);
                }
                else if (field == "employeename")
                {
                    DataTable dt = db.QueryTable("select id from employee where content->>'name' ilike '%" + f + "%'");
                    string ids = dt.GetIds();
                    sb.AppendFormat(" and (content->>'employeeid')::int in ({0})", ids);
                }
                else if (field == "comment")
                {
                    sb.AppendFormat(" and content->>'comment' ilike '%{0}%'", f);
                }
                else if (field == "status")
                {
                    if (f == "draft") sb.Append(" and (content->>'auditstatus')::int=0");
                    else if (f == "audited") sb.Append(" and (content->>'auditstatus')::int=1");
                }

            }

            StringBuilder sborder = new StringBuilder();
            sborder.Append(" order by content->>'billdate' desc,id desc ");


            sborder.AppendFormat(" limit {0} offset {1} ", pagesize, pagesize * pageindex - pagesize);

            string sql = "select *  from bill "
                        + sb.ToString()
                        + sborder.ToString();

            string sqlCount = "select count(0) as cnt from bill "
                        + sb.ToString();

            int recordcount = db.Count(sqlCount);
            List<TableModel> list = db.Where(sql);
            foreach (var item in list)
            {
                item.content.Add("makername", db.First("employee", item.content.Value<int>("makerid")).content.Value<string>("name"));
                item.content.Add("vendorname", db.First("vendor", item.content.Value<int>("vendorid")).content.Value<string>("name"));
                item.content.Add("employeename", db.First("employee", item.content.Value<int>("employeeid")).content.Value<string>("name"));

            }

            return new { resulttotal = recordcount, data = list };
        }

        public Object LoadBill(string parameters)
        {
            ParameterHelper ph = new ParameterHelper(parameters);
            int id = ph.GetParameterValue<int>("id");

            DBHelper db = new DBHelper();
            var bill = db.First("bill", id);
            var maker = db.First("employee", bill.content.Value<int>("makerid"));
            var vendor = db.First("vendor", bill.content.Value<int>("vendorid"));
            var employee = db.First("employee", bill.content.Value<int>("employeeid"));
             
            var option = db.First("select * from option");
            int digit = 2;
            bool noeditbillcode = option.content["noeditbillcode"] == null ? false : option.content.Value<bool>("noeditbillcode");
            bool noeditbilldate = option.content["noeditbilldate"] == null ? false : option.content.Value<bool>("noeditbilldate");

            return new
            {
                content = bill.content,
                makername = maker.content.Value<string>("name"),
                digit = digit,
                vendorname = vendor.content.Value<string>("name"),
                employeename = employee.content.Value<string>("name"),
                noeditbillcode = noeditbillcode,
                noeditbilldate = noeditbilldate
            };
        }


        [MyLog("应付调整单编号设置")]
        public Object BillCodeConfigSave(string parameters)
        {
            ParameterHelper ph = new ParameterHelper(parameters);
            string template = ph.GetParameterValue<string>("template");

            using (DBHelper db = new DBHelper())
            {
                //获取编号配置
                var config = db.FirstOrDefault("select * from billconfig where content->>'billname'='" + billname + "'");
                if (config == null)
                {
                    TableModel billconfig = new TableModel();
                    billconfig.content = new JObject();
                    billconfig.content.Add("billcodetemplate", template);

                    db.Add("billconfig", billconfig);
                }
                else
                {
                    config.content["billcodetemplate"] = template;

                    db.Edit("billconfig", config);
                }

                db.SaveChanges();
            }
            return new { message = "ok", newcode = BillHelper.GetBillCode(billname) };

        }

        [MyLog("新增应付调整单")]
        public Object AddSave(string parameters)
        {
            TableModel model = JsonConvert.DeserializeObject<TableModel>(parameters);

            using (DBHelper db = new DBHelper())
            {
                //检查是否已经开账
                var options = db.First("select * from option");
                if (options.content["initoverdate"] == null)
                {
                    return new { message = StringHelper.GetString("系统还没有开账，不能保存单据！") };
                }

                //单据日期不能早于开账日期
                if (model.content.Value<DateTime>("billdate").CompareTo(options.content.Value<DateTime>("initoverdate")) < 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于开账日期！") };
                }

                //单据日期不能早于月结存日期
                var lastbalance = db.FirstOrDefault("select * from monthbalance order by id desc");
                if (lastbalance != null
                    && model.content.Value<DateTime>("billdate").CompareTo(lastbalance.content.Value<DateTime>("balancedate")) <= 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于月结存日期！") };
                }


                //检查编号重复
                int cnt = db.Count("select count(0) as cnt from bill where content->>'billname'='" + billname + "' and content->>'billcode'='" + model.content.Value<string>("billcode") + "'");
                if (cnt > 0)
                {
                    return new { message = StringHelper.GetString("编号有重复") };
                }
 

                model.content["billname"] = billname;
                model.content["makerid"] = PluginContext.Current.Account.Id;
                model.content["createtime"] = DateTime.Now;
                model.content["auditstatus"] = 0;

                db.Add("bill", model);
                db.SaveChanges();

                bool nodraftprint = options.content["nodraftprint"] != null && options.content.Value<bool>("nodraftprint");

                return new { message = "ok", id = model.id, nodraftprint = nodraftprint };
            }
        }

        [MyLog("保存并审核应付调整单")]
        public Object AddAuditSave(string parameters)
        {
            TableModel model = JsonConvert.DeserializeObject<TableModel>(parameters);

            using (DBHelper db = new DBHelper())
            {
                //检查是否已经开账
                var options = db.First("select * from option");
                if (options.content["initoverdate"] == null)
                {
                    return new { message = StringHelper.GetString("系统还没有开账，不能保存单据！") };
                }

                //单据日期不能早于开账日期
                if (model.content.Value<DateTime>("billdate").CompareTo(options.content.Value<DateTime>("initoverdate")) < 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于开账日期！") };
                }

                //单据日期不能早于月结存日期
                var lastbalance = db.FirstOrDefault("select * from monthbalance order by id desc");
                if (lastbalance != null
                    && model.content.Value<DateTime>("billdate").CompareTo(lastbalance.content.Value<DateTime>("balancedate")) <= 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于月结存日期！") };
                }

                //检查编号重复
                int cnt = db.Count("select count(0) as cnt from bill where content->>'billname'='" + billname + "' and content->>'billcode'='" + model.content.Value<string>("billcode") + "'");
                if (cnt > 0)
                {
                    return new { message = StringHelper.GetString("编号有重复") };
                }
                 

                model.content["billname"] = billname;
                model.content["makerid"] = PluginContext.Current.Account.Id;
                model.content["createtime"] = DateTime.Now;
                model.content["auditorid"] = PluginContext.Current.Account.Id;
                model.content["audittime"] = DateTime.Now;
                model.content["auditstatus"] = 1;

                db.Add("bill", model);
 
                model.Audit(db);
                 
                db.SaveChanges();
            }
            return new { message = "ok", id = model.id };

        }

        [MyLog("编辑应付调整单")]
        public Object EditSave(string parameters)
        {
            TableModel model = JsonConvert.DeserializeObject<TableModel>(parameters);

            using (DBHelper db = new DBHelper())
            {
                //检查是否已经开账
                var options = db.First("select * from option");
                if (options.content["initoverdate"] == null)
                {
                    return new { message = StringHelper.GetString("系统还没有开账，不能保存单据！") };
                }

                //单据日期不能早于开账日期
                if (model.content.Value<DateTime>("billdate").CompareTo(options.content.Value<DateTime>("initoverdate")) < 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于开账日期！") };
                }

                //单据日期不能早于月结存日期
                var lastbalance = db.FirstOrDefault("select * from monthbalance order by id desc");
                if (lastbalance != null
                    && model.content.Value<DateTime>("billdate").CompareTo(lastbalance.content.Value<DateTime>("balancedate")) <= 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于月结存日期！") };
                }


                //检查编号重复
                int cnt = db.Count("select count(0) as cnt from bill where id<>" + model.id + " and content->>'billname'='" + billname + "' and content->>'billcode'='" + model.content.Value<string>("billcode") + "'");
                if (cnt > 0)
                {
                    return new { message = StringHelper.GetString("编号有重复") };
                }
 

                //model.content["billname"] = billname;
                model.content["makerid"] = PluginContext.Current.Account.Id;
                //model.content["createtime"] = DateTime.Now;
                 
                db.Edit("bill", model);
                db.SaveChanges();

                bool nodraftprint = options.content["nodraftprint"] != null && options.content.Value<bool>("nodraftprint");

                return new { message = "ok", id = model.id, nodraftprint = nodraftprint };
            }
        }

        [MyLog("编辑并审核应付调整单")]
        public Object EditAuditSave(string parameters)
        {
            TableModel model = JsonConvert.DeserializeObject<TableModel>(parameters);

            using (DBHelper db = new DBHelper())
            {
                //检查是否已经审核过
                if (model.content["auditorid"] != null)
                {
                    return new { message = StringHelper.GetString("单据已经审核过，不能修改！") };
                }

                //检查是否已经开账
                var options = db.First("select * from option");
                if (options.content["initoverdate"] == null)
                {
                    return new { message = StringHelper.GetString("系统还没有开账，不能保存单据！") };
                }

                //单据日期不能早于开账日期
                if (model.content.Value<DateTime>("billdate").CompareTo(options.content.Value<DateTime>("initoverdate")) < 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于开账日期！") };
                }

                //单据日期不能早于月结存日期
                var lastbalance = db.FirstOrDefault("select * from monthbalance order by id desc");
                if (lastbalance != null
                    && model.content.Value<DateTime>("billdate").CompareTo(lastbalance.content.Value<DateTime>("balancedate")) <= 0)
                {
                    return new { message = StringHelper.GetString("单据日期不能早于月结存日期！") };
                }

                //检查编号重复
                int cnt = db.Count("select count(0) as cnt from bill where id<>" + model.id + " and content->>'billname'='" + billname + "' and content->>'billcode'='" + model.content.Value<string>("billcode") + "'");
                if (cnt > 0)
                {
                    return new { message = StringHelper.GetString("编号有重复") };
                }
 

                //model.content["billname"] = billname;
                model.content["makerid"] = PluginContext.Current.Account.Id;
                //model.content["createtime"] = DateTime.Now;
                model.content["auditorid"] = PluginContext.Current.Account.Id;
                model.content["audittime"] = DateTime.Now;
                model.content["auditstatus"] = 1;

                db.Edit("bill", model);
                 

                model.Audit(db);

                db.SaveChanges();
            }
            return new { message = "ok", id = model.id };

        }

        public Object RegisterPrintModel(string parameters)
        {
            ParameterHelper ph = new ParameterHelper(parameters);
            int billid = ph.GetParameterValue<int>("billid");

            DBHelper db = new DBHelper();
            var bill = db.First("bill", billid);
            var employee = db.First("employee", bill.content.Value<int>("employeeid"));
            var maker = db.First("employee", bill.content.Value<int>("makerid"));
            var vendor = db.First("vendor", bill.content.Value<int>("vendorid"));

            decimal total = bill.content.Value<decimal>("total");

            PrintData pd = new PrintData();
            pd.HeaderField = new List<string>() 
            {
                "公司名称",
                "单据编号",
                "单据日期",
                "经手人", 
                "供应商名称",
                "供应商联系人",
                "供应商电话",
                "供应商地址", 
                "备注",
                "系统日期",
                "系统时间",
                "应付调整金额",
                "应付调整金额大写"
            };

            pd.HeaderValue = new Dictionary<string, string>()
            {
                {"公司名称",PluginContext.Current.Account.CompanyName},
                {"单据编号",bill.content.Value<string>("billcode")},
                {"单据日期",bill.content.Value<string>("billdate")},
                {"经手人",employee.content.Value<string>("name")}, 
                {"供应商名称",vendor.content.Value<string>("name")},
                {"供应商联系人",vendor.content.Value<string>("linkman")},
                {"供应商电话",vendor.content.Value<string>("linkmobile")},
                {"供应商地址",vendor.content.Value<string>("address")}, 
                {"备注",bill.content.Value<string>("comment")},
                {"系统日期",DateTime.Now.ToString("yyyy-MM-dd")},
                {"系统时间",DateTime.Now.ToString("yyyy-MM-dd HH:mm")},
                {"应付调整金额",total.ToString("n2")},
                {"应付调整金额大写",MoneyHelper.ConvertSum(total)} 
            };

            pd.DetailField = new List<string>();
            pd.DetailValue = new List<Dictionary<string, string>>();
             
            PrintManager pm = new PrintManager(PluginContext.Current.Account.ApplicationId);
            int modelid = pm.RegisterPrintModel(pd);

            return new { modelid = modelid };
        }


    }
}
