using NLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CIBNPA_Statistics
{
    class Program
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        static DateTime now = DateTime.Now;
        static string sDate = string.Empty, eDate = string.Empty;
        static string resultDir = string.Empty; //檔案路徑
        private static string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        static string[] fileList;
        static List<KeyValuePair<string, int>> countList = new List<KeyValuePair<string, int>>();
        static List<CSVResult> csvList = new List<CSVResult>();
        static string finalCSVName = string.Empty;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                //取當月
                //now = now.AddMonths(-1);
                sDate = now.Date.AddDays(1 - now.Day).ToString("yyyyMMdd");
                eDate = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyyMMdd");
            }
            else if (args.Length == 2)
            {
                sDate = args[0] + args[1] + "01";
                eDate = args[0] + args[1] + DateTime.DaysInMonth(int.Parse(args[0]), int.Parse(args[1]));
            }
            logger.Info($"統計起訖日期為:{sDate}~{eDate}");

            GetResultCSV();
            ReadResultCSV();
            SaveFinalCSV();
            SendMail(fileList, finalCSVName);
        }

        /// <summary>
        /// 取得目標月檔案
        /// </summary>
        static void GetResultCSV()
        {
            try
            {
                resultDir = Path.Combine(assemblyPath, "../" + ConfigurationManager.AppSettings.Get("filestore_result_dir"));
                logger.Info($"開始取得 {resultDir} 檔案");    //取得目標月檔案
                fileList = Directory.GetFiles(resultDir, "*_" + sDate.Substring(0, 6) + "??_OK5.csv");
            }
            catch (Exception ex)
            {
                logger.Error($"GetResultCSV 發生錯誤");
                logger.Fatal($"GetResultCSV : {ex}");
                throw;
            }
        }

        /// <summary>
        /// 讀取該月檔案
        /// </summary>
        static void ReadResultCSV()
        {
            try
            {
                if (fileList.Length > 0)
                {
                    logger.Info($"開始統計 {fileList.Length} 個檔案");    //取得目標月檔案
                    for (int i = 0; i < fileList.Length; i++)
                    {
                        int counter = -1;
                        string fileName = Path.GetFileName(fileList[i]);
                        var m = Regex.Match(fileName, @"(?<=.*_)\d{3}(?=_)");   //取得單位代號
                        string bankCode = m.Groups[0].Value;
                        using (StreamReader file = new StreamReader(fileList[i]))
                        {
                            while (file.ReadLine() != null)
                            {
                                counter++;
                            }
                            file.Close();
                        }

                        if (countList.Any(x => x.Key.Equals(bankCode))) //若已有數量則累計
                        {
                            int newCount = countList.First(kvp => kvp.Key == bankCode).Value + counter;
                            countList.RemoveAll(x => x.Key.Contains(bankCode)); //但只能移除再寫入
                            countList.Add(new KeyValuePair<string, int>(bankCode, newCount));
                        }
                        else
                            countList.Add(new KeyValuePair<string, int>(bankCode, counter));
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error($"ReadResultCSV 發生錯誤");
                logger.Fatal($"ReadResultCSV : {ex}");
                throw;
            }
        }

        /// <summary>
        /// 產生最後檔案
        /// </summary>
        static void SaveFinalCSV()
        {
            try
            {
                logger.Info($"開始儲存檔案");

                countList = countList.OrderBy(x => x.Key).ToList();

                foreach (string line in File.ReadLines(Path.Combine(assemblyPath, ConfigurationManager.AppSettings.Get("template_file"))))
                {
                    CSVResult c = new CSVResult();
                    c._bankCode = line.Trim();
                    c._dataDate = eDate;
                    if (countList.Any(x => x.Key.Equals(line.Trim())))
                        c._count = countList.First(kvp => kvp.Key == line.Trim()).Value.ToString();
                    else
                        c._count = "0";

                    csvList.Add(c);
                }

                Encoding utf8 = new UTF8Encoding(false);
                using (StreamWriter writer = new StreamWriter(new FileStream(Path.Combine(Path.Combine(assemblyPath, "NAP567_" + (int.Parse(sDate.Substring(0, 6)) - 191100) + ".csv")), FileMode.Create), utf8))
                {
                    foreach (CSVResult r in csvList)
                    {
                        writer.WriteLine(r._bankCode + "," + r._dataDate + "," + r._count);
                    }
                }

                //僅列出有在檔案清單的
                //using (StreamWriter writer = new StreamWriter(new FileStream(Path.Combine(Path.Combine(assemblyPath, "NAP567_" + (int.Parse(sDate.Substring(0, 6)) - 191100) + ".csv")), FileMode.Create), utf8))
                //{
                //    foreach (var r in countList)
                //    {
                //        writer.WriteLine(r.Key + "," + eDate + "," + r.Value);
                //    }
                //}
                finalCSVName = "NAP567_" + (int.Parse(sDate.Substring(0, 6)) - 191100) + ".csv";
                logger.Info($"完成 : {finalCSVName}");
            }
            catch (Exception ex)
            {
                logger.Error($"SaveFinalCSV 發生錯誤");
                logger.Fatal($"SaveFinalCSV : {ex}");
                throw;
            }
        }


        /// <summary>
        /// 發mail
        /// </summary>
        /// <param name="MailData"></param>
        /// <param name="strMailTo"></param>
        /// <param name="strSubject"></param>
        /// <returns></returns>
        static void SendMail(string[] MailData, string strSubject)
        {
            try
            {
                logger.Info($"開始寄mail");
                if (MailData != null)
                {
                    string strFom = ConfigurationManager.AppSettings["UserFrom"];
                    string strMailAccount = ConfigurationManager.AppSettings["MailAccount"];
                    string strMailPW = ConfigurationManager.AppSettings["MailPassword"];
                    string strSSL = ConfigurationManager.AppSettings["MailSSL"];
                    string strSmtp = ConfigurationManager.AppSettings["SMTP"];
                    int strSmtpPort = Convert.ToInt16(ConfigurationManager.AppSettings["SMTPPort"]);
                    string strMailTo = ConfigurationManager.AppSettings["SendTo"];

                    string strFromName = string.Empty;

                    using (System.Net.Mail.MailMessage message = new System.Net.Mail.MailMessage())
                    {

                        message.SubjectEncoding = System.Text.Encoding.UTF8;
                        message.BodyEncoding = System.Text.Encoding.UTF8;
                        string[] mailTo = strMailTo.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < mailTo.Length; i++)
                            message.To.Add(mailTo[i]);

                        message.Bcc.Add("jamie.lin@afisc.com.tw");

                        message.From = new System.Net.Mail.MailAddress(strFom, strFromName);
                        message.Subject = $"金融資料調閱系統查詢種類6/7 {sDate.Substring(0, 6)} 回覆筆數統計結果";
                        message.IsBodyHtml = true;

                        //string sCustTemplete = HttpContext.Current.Server.MapPath("~/Document/" + strTemplete);
                        //using (System.IO.TextReader txtRead = new System.IO.StreamReader(sCustTemplete))
                        //{
                        //    message.Body = txtRead.ReadToEnd();
                        //}
                        var att = new System.Net.Mail.Attachment(Path.Combine(assemblyPath, finalCSVName));
                        message.Attachments.Add(att);
                        message.Body = $"附件為 金融資料調閱系統查詢種類6/7 {sDate.Substring(0, 6)} 回覆筆數統計結果 : {finalCSVName}" + "<br>";
                        message.Body += "<br>";
                        message.Body += "統計的檔案為以下 :" + "<br>";
                        string strBody = string.Empty;
                        for (int i = 0; i < MailData.Length; i++)
                        {
                            strBody = string.IsNullOrEmpty(Convert.ToString(MailData[i])) ? "" : MailData[i].ToString();
                            //message.Body = message.Body.Replace("{" + i + "}", strBody);
                            strBody = Path.GetFileName(strBody);
                            message.Body += strBody + "<br>";
                        }

                        message.Body += "<br><br>";                        
                        message.Body += "請卓參。";

                        using (System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient(strSmtp, strSmtpPort))
                        {
                            smtp.Timeout = 60 * 1000;
                            if (strSSL == "N")
                            {
                                smtp.EnableSsl = false;
                            }
                            else
                                smtp.EnableSsl = true;
                            if (!string.IsNullOrEmpty(strMailAccount) && !string.IsNullOrEmpty(strMailPW))
                                smtp.Credentials = new NetworkCredential(strMailAccount, strMailPW);
                            smtp.Send(message);
                        }
                    }
                }
                logger.Info($"寄mail完成");
            }
            catch (Exception ex)
            {
                logger.Error($"SendMail 發生錯誤");
                logger.Fatal($"SendMail : {ex}");
            }
        }

        class CSVResult
        {
            public string _bankCode { get; set; }
            public string _dataDate { get; set; }
            public string _count { get; set; }
        }
    }
}
