namespace PrintAgent.Core
{
    // Строка чека из задания печати; заполняются только поля, нужные её типу
    public class ReceiptLine
    {
        public string Type;
        public string Text;
        public string Align;
        public bool Bold;
        public int Size;
        public string Left;
        public string Right;
        public string Value;
        public int Lines;
    }
}
