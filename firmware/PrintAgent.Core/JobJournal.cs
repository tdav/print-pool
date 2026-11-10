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

        /// <summary>Итог можно больше не повторять: принят (204), задания нет (404) или оно уже закрыто (409).</summary>
        public static bool IsDelivered(int httpStatus)
        {
            return httpStatus == 204 || httpStatus == 404 || httpStatus == 409;
        }
    }
}
