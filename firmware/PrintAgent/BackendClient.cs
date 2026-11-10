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
        public LineDto[] lines { get; set; }
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
        private readonly string deviceId;
        private readonly HttpClient http;

        public BackendClient(string baseUrl, string deviceId)
        {
            this.baseUrl = baseUrl;
            this.deviceId = deviceId;
            this.http = new HttpClient();
            this.http.Timeout = TimeSpan.FromSeconds(10);
        }

        // Возвращает код HTTP; 0 — бэкенд не ответил. При 200 jobId задан всегда,
        // а lines равен null, если строки задания не удалось разобрать.
        public int TryGetNext(out string jobId, out ReceiptLine[] lines)
        {
            jobId = null;
            lines = null;
            string body;
            try
            {
                using (HttpResponseMessage response = this.http.Get(this.baseUrl + "/api/devices/" + this.deviceId + "/jobs/next"))
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
            }

            // Без ID задания итог сообщить некому: считаем, что задания нет, бэкенд снимет его по таймауту.
            return jobId == null || jobId.Length == 0 ? 204 : 200;
        }

        // Возвращает код HTTP; 0 — бэкенд не ответил.
        public int PostResult(string jobId, bool success, string error)
        {
            string json = "{\"deviceId\":\"" + this.deviceId + "\",\"success\":" + (success ? "true" : "false")
                + (error == null ? "" : ",\"error\":\"" + error + "\"") + "}";
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
