﻿using System;
using System.Collections.Generic;
using System.Text;

namespace vinkekfish.CSharp_help
{
    // https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-7-3
    class Help1
    {
        // fixed-поля
        unsafe struct S
        {
            public fixed int myFixedField[10];
        }

        class ObjectInit
        {
            public string Name;
        }

        static S s = new S();
        unsafe public void M()
        {
            int p = s.myFixedField[5];

            // stackalloc инициализаторы
            int* pArr = stackalloc int[3] {1, 2, 3};

            // Кортежи
            (double, int) t1 = (4.5, 3);
            Console.WriteLine(t1);

            // Инициализатор переменной out прямо в вызове функции
            func1(out int k);

            // Инициализатор объекта
            new ObjectInit() {Name = "Init string"};
        }

        public static void func1(out int k)
        {
             k = 0;
        }
    }
}
