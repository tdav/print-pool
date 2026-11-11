using System;
using System.Net.Http;
using System.Text;
using nanoFramework.Json;
using PrintAgent.Core;

namespace PrintAgent
{
    // Задание в том виде, как его отдаёт бэкенд.
    // Имена свойств совпадают с ключами JSON буква в букву: разбор на плате чувствителен к регистру.
    public class JobDto
    {
        public string jobId { get; set; }
        public string type { get; set; }
        public int serverTime { get; set; }
        public LineDto[] lines { get; set; }
        public DataDto data { get; set; }
    }

    // Данные заказа для чеков kitchen и bill.
    public class DataDto
    {
        public string title { get; set; }
        public string billNumber { get; set; }
        public string table { get; set; }
        public string hall { get; set; }
        public string cashier { get; set; }
        public ItemDto[] items { get; set; }
        public int subtotal { get; set; }
        public int servicePercent { get; set; }
        public int serviceSum { get; set; }
        public int total { get; set; }
        public string currency { get; set; }
    }

    public class ItemDto
    {
        public string name { get; set; }
        public string quantity { get; set; }
        public int sum { get; set; }
    }

    public class LineDto
    {
        public string type { get; set; }
        public string text { get; set; }
        public string align { get; set; }
        public bool bold { get; set; }
        public int size { get; set; }
        public string left { get; set; }
        public string right { get; set; }
        public string value { get; set; }
        public int lines { get; set; }
    }

    // HTTP-клиент бэкенда: забрать задание и сообщить итог печати.
    public class BackendClient
    {
        private readonly string baseUrl;
        // Настройки, а не копия ID: новый ID из админ-панели действует со следующего запроса.
        private readonly AgentConfig config;
        private readonly HttpClient http;

        public BackendClient(string baseUrl, AgentConfig config)
        {
            this.baseUrl = baseUrl;
            this.config = config;
            this.http = new HttpClient();
            this.http.Timeout = TimeSpan.FromSeconds(10);
        }

        // Возвращает код HTTP; 0 — бэкенд не ответил. При 200 jobId задан всегда.
        // type — "lines", "kitchen" или "bill" (задание прежнего бэкенда без типа считается "lines").
        // lines и data равны null, если их нет в задании или они не разобрались.
        public int TryGetNext(out string jobId, out string type, out int serverTime, out ReceiptLine[] lines, out ReceiptData data)
        {
            jobId = null;
            type = null;
            serverTime = 0;
            lines = null;
            data = null;
            string body;
            try
            {
                using (HttpResponseMessage response = this.http.Get(this.baseUrl + "/api/devices/" + this.config.DeviceId + "/jobs/next"))
                {
                    int status = (int)response.StatusCode;
                    if (status != 200)
                    {
                        return status;
                    }

                    body = response.Content.ReadAsString();
                }
            }
            catch (Exception)
            {
                return 0;
            }

            try
            {
                JobDto job = (JobDto)JsonConvert.DeserializeObject(body, typeof(JobDto));
                jobId = job.jobId;
                type = job.type == null || job.type.Length == 0 ? "lines" : job.type;
                serverTime = job.serverTime;
                // Данные заказа без позиций не печатаются: data остаётся null, задание получит BAD_JOB.
                if (job.data != null && job.data.items != null && job.data.items.Length > 0)
                {
                    ReceiptData order = new ReceiptData();
                    order.Title = job.data.title;
                    order.BillNumber = job.data.billNumber;
                    order.Table = job.data.table;
                    order.Hall = job.data.hall;
                    order.Cashier = job.data.cashier;
                    order.Currency = job.data.currency;
                    order.Subtotal = job.data.subtotal;
                    order.ServicePercent = job.data.servicePercent;
                    order.ServiceSum = job.data.serviceSum;
                    order.Total = job.data.total;
                    order.Items = new ReceiptItem[job.data.items.Length];
                    for (int i = 0; i < order.Items.Length; i++)
                    {
                        ItemDto dto = job.data.items[i];
                        ReceiptItem item = new ReceiptItem();
                        item.Name = dto.name;
                        item.Quantity = dto.quantity;
                        item.Sum = dto.sum;
                        order.Items[i] = item;
                    }

                    data = order;
                }

                if (job.lines != null)
                {
                    ReceiptLine[] result = new ReceiptLine[job.lines.Length];
                    for (int i = 0; i < result.Length; i++)
                    {
                        LineDto dto = job.lines[i];
                        ReceiptLine line = new ReceiptLine();
                        line.Type = dto.type;
                        line.Text = dto.text;
                        line.Align = dto.align;
                        line.Bold = dto.bold;
                        line.Size = dto.size;
                        line.Left = dto.left;
                        line.Right = dto.right;
                        line.Value = dto.value;
                        line.Lines = dto.lines;
                        result[i] = line;
                    }

                    lines = result;
                }
            }
            catch (Exception)
            {
                lines = null;
                data = null;
            }

            // Без ID задания итог сообщить некому: считаем, что задания нет, бэкенд снимет его по таймауту.
            return jobId == null || jobId.Length == 0 ? 204 : 200;
        }

        // Возвращает код HTTP; 0 — бэкенд не ответил.
        public int PostResult(string jobId, bool success, string error)
        {
            string json = "{\"deviceId\":" + AdminServer.Quote(this.config.DeviceId) + ",\"success\":" + (success ? "true" : "false")
                + (error == null ? "" : ",\"error\":" + AdminServer.Quote(error)) + "}";
            try
            {
                using (HttpResponseMessage response = this.http.Post(
                    this.baseUrl + "/api/jobs/" + jobId + "/result",
                    new StringContent(json, Encoding.UTF8, "application/json")))
                {
                    return (int)response.StatusCode;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
