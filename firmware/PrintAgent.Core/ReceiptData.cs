namespace PrintAgent.Core
{
    // Позиция заказа; количество приходит с бэкенда готовой печатной строкой ("0,35")
    public class ReceiptItem
    {
        public string Name;
        public string Quantity;
        public int Sum;
    }

    // Данные заказа для чека типа kitchen или bill; плата заполняет их из JSON
    public class ReceiptData
    {
        public string Title, BillNumber, Table, Hall, Cashier, Currency;
        public ReceiptItem[] Items;
        public int Subtotal, ServicePercent, ServiceSum, Total;
    }
}
