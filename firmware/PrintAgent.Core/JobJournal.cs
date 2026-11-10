namespace PrintAgent.Core
{
    /// <summary>Запись журнала печати, сохраняемая во флеш. Только поля и простые типы.</summary>
    public class JournalEntry
    {
        public string JobId;
        public string State;     // JobJournal.Started или JobJournal.Finished
        public bool Success;     // имеет смысл для finished
        public string Error;     // код ошибки или null
    }

    /// <summary>Решения журнала печати: защита от повторной печати чека после потери питания.</summary>
    public static class JobJournal
    {
        public const string Started = "started";
        public const string Finished = "finished";

        /// <summary>Что сообщить бэкенду при включении, или null, если сообщать нечего.</summary>
        public static JournalEntry OnBoot(JournalEntry saved)
        {
            if (saved == null || saved.JobId == null || saved.JobId.Length == 0)
            {
                return null;
            }

            if (saved.State == Finished)
            {
                // Печать завершена, но итог не был доставлен: повторяем его же
                return saved;
            }

            // Любое другое состояние (started, повреждённое, неизвестное) считается прерванной печатью:
            // молча забыть задание нельзя, бэкенд выдал бы его снова и чек напечатался бы дважды
            JournalEntry failure = new JournalEntry();
            failure.JobId = saved.JobId;
            failure.State = Finished;
            failure.Success = false;
            failure.Error = "REBOOT_DURING_PRINT";
            return failure;
        }

        /// <summary>
        /// Текст записи для файла: "started|ID|." или "finished|ID|1 или 0|код ошибки|.".
        /// Маркер "|." в конце отличает целую запись от оборванной при потере питания.
        /// </summary>
        public static string Format(JournalEntry entry)
        {
            if (entry.State != Finished)
            {
                return Started + "|" + entry.JobId + "|.";
            }

            return Finished + "|" + entry.JobId + "|" + (entry.Success ? "1" : "0") + "|" + (entry.Error == null ? "" : entry.Error) + "|.";
        }

        /// <summary>Разбор записи из файла; null, если файла нет или запись оборвана.</summary>
        public static JournalEntry Parse(string text)
        {
            if (text == null || text.Length < 2 || text[text.Length - 1] != '.' || text[text.Length - 2] != '|')
            {
                return null;
            }

            string[] parts = text.Split('|');
            if (parts.Length < 3)
            {
                return null;
            }

            JournalEntry entry = new JournalEntry();
            entry.State = parts[0];
            entry.JobId = parts[1];
            entry.Success = parts.Length > 3 && parts[2] == "1";
            entry.Error = parts.Length > 4 && parts[3].Length > 0 ? parts[3] : null;
            return entry;
        }

        /// <summary>Итог можно больше не повторять: принят (204), задания нет (404) или оно уже закрыто (409).</summary>
        public static bool IsDelivered(int httpStatus)
        {
            return httpStatus == 204 || httpStatus == 404 || httpStatus == 409;
        }
    }
}
