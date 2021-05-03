﻿using System;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace vinkekfish
{
    /*
     * Этот класс является предком остальных
     * Классы не предназначены для изменений
     * Чтобы их изменять, по хорошему, надо создать новый класс с другой датой создания и добавить его в тесты
     * Наследники этого класса: Keccak_base_*
     * */
    public unsafe abstract class Keccak_abstract
    {
        public const int S_len = 5;
        public const int S_len2 = S_len*S_len;

        // Это внутреннее состояние keccak, а также вспомогательные переменные, не являющиеся состоянием
        // Здесь сначала идёт B, потом C, потом S.
        // При перезаписи после конца с высокой вероятностью пострадает S, что даст возможность тестам сделать своё дело
        /// <summary>Внутреннее состояние keccak. Используйте KeccakStatesArray для того, чтобы разбить его на указатели</summary>
        protected readonly byte[] State = new byte[(S_len2 + S_len + S_len2) << 3];
        protected          ulong    d;

        /// <summary>Фиксирует объект State и создаёт на него ссылки
        /// using (var state = new KeccakStatesArray(State))
        /// state.S и другие</summary>
        public class KeccakStatesArray : IDisposable
        {
            // Желательно вызывать ClearAfterUser явно поименованно, чтобы показать, что очистка идёт
            public KeccakStatesArray(byte[] State, bool ClearAfterUse = true)
            {
                this.ClearAfterUse = ClearAfterUse;

                handle = GCHandle.Alloc(State, GCHandleType.Pinned);
                CountToCheck++;

                Base  = (byte *) handle.AddrOfPinnedObject().ToPointer();
                B     = Base;
                C     = B + (S_len2 << 3);
                S     = C + (S_len  << 3);

                Slong = (ulong *) S;
                Blong = (ulong *) B;
                Clong = (ulong *) C;
                Size  = State.LongLength;
            }

            public readonly GCHandle handle;
            public byte * S, B, C, Base;
            public ulong * Slong, Blong, Clong;
            public long Size;

            public readonly bool ClearAfterUse;
            protected bool Disposed = false;
            protected static   int  CountToCheck = 0;
            public static int getCountToCheck => CountToCheck;
            public void Dispose()
            {
                if (!Disposed)
                try
                {
                    if (ClearAfterUse)
                        BytesBuilder.ToNull(targetLength: Size, t: Base);

                    Disposed = true; // TODO: Проверить срабатывание финализатора без этого участка кода
                    CountToCheck--;
                }
                finally
                {
                    handle.Free();
                }
            }

            ~KeccakStatesArray()
            {
                if (!Disposed)
                    throw new Exception("Keccak_abstract.KeccakStatesArray: not all KeccakStatesArray is disoposed");
            }
        }

        public abstract Keccak_abstract Clone();
        /// <summary>Дополнительно очищает состояние объекта после вычислений.
        /// Рекомендуется вручную вызывать Clear5 и Clear5x5 до выхода из fixed, чтобы GC не успел их переместить (скопировать) до очистки</summary>
        /// <param name="GcCollect">Если true, то override реализации должны дополнительно попытаться перезаписать всю память программы. <see langword="abstract"/> реализация ничего не делает</param>
        public virtual void Clear(bool GcCollect = true)
        {
            ClearState();
        }

        public virtual void ClearState()
        {
            BytesBuilder.ToNull(State);
            ClearStateWithoutStateField();
        }

        public virtual void ClearStateWithoutStateField()
        {
            this.d = 0;
        }

        public virtual void init()
        {
            using (var state = new KeccakStatesArray(State))
                Clear5x5(state.Slong);
        }

        /// <summary>Этот метод может использоваться для очистки матриц S и B после вычисления последнего шага хеша</summary>
        /// <param name="S">Очищаемая матрица размера 5x5 *ulong</param>
        public unsafe static void Clear5x5(ulong * S)
        {
            var len = S_len * S_len;
            var se  = S + len;
            for (; S < se; S++)
                *S = 0;
        }

        /// <summary>Этот метод может использоваться для очистки вспомогательного массива C</summary>
        /// <param name="C">Очищаемый массив размера 5*ulong</param>
        public unsafe static void Clear5(ulong * C)
        {
            var se  = C + S_len;
            for (; C < se; C++)
                *C = 0;
        }


        public const int   r_512  = 576;
        public const int   r_512b = r_512 >> 3; // 72
        public const int   r_512s = r_512 >> 6; // 9

        public static readonly int[]   rNumbers = {1152, 1088, 832, 576}; // 224, 256, 384, 512 битов
        public static readonly ulong[] RC =
        {
            0x0000000000000001,
            0x0000000000008082,
            0x800000000000808A,
            0x8000000080008000,
            0x000000000000808B,
            0x0000000080000001,

            0x8000000080008081,
            0x8000000000008009,
            0x000000000000008A,
            0x0000000000000088,


            0x0000000080008009,
            0x000000008000000A,
            0x000000008000808B,
            0x800000000000008B,
            0x8000000000008089,

            0x8000000000008003,
            0x8000000000008002,
            0x8000000000000080,
            0x000000000000800A,
            0x800000008000000A,


            0x8000000080008081,
            0x8000000000008080,
            0x0000000080000001,
            0x8000000080008008
        };

        // Реализация раундов keccak и раундового преобразования
        // Раундовое преобразование
        public unsafe void roundB(ulong * a, ulong * c, ulong * b)
        {
            //шаг θ
            *(c + 0) = *(a +  0) ^ *(a +  1) ^ *(a +  2) ^ *(a +  3) ^ *(a +  4);
            *(c + 1) = *(a +  5) ^ *(a +  6) ^ *(a +  7) ^ *(a +  8) ^ *(a +  9);
            *(c + 2) = *(a + 10) ^ *(a + 11) ^ *(a + 12) ^ *(a + 13) ^ *(a + 14);
            *(c + 3) = *(a + 15) ^ *(a + 16) ^ *(a + 17) ^ *(a + 18) ^ *(a + 19);
            *(c + 4) = *(a + 20) ^ *(a + 21) ^ *(a + 22) ^ *(a + 23) ^ *(a + 24);

            d = *(c + 4) ^ ((*(c + 1) << 1) | (*(c + 1) >> 63));
            *(a +  0) ^= d; // D[0];
            *(a +  1) ^= d; // D[0];
            *(a +  2) ^= d; // D[0];
            *(a +  3) ^= d; // D[0];
            *(a +  4) ^= d; // D[0];

            d = *(c + 0) ^ ((*(c + 2) << 1) | (*(c + 2) >> 63));
            *(a +  5) ^= d; // D[1];
            *(a +  6) ^= d; // D[1];
            *(a +  7) ^= d; // D[1];
            *(a +  8) ^= d; // D[1];
            *(a +  9) ^= d; // D[1];

            d = *(c + 1) ^ ((*(c + 3) << 1) | (*(c + 3) >> 63));
            *(a + 10) ^= d; // D[2];
            *(a + 11) ^= d; // D[2];
            *(a + 12) ^= d; // D[2];
            *(a + 13) ^= d; // D[2];
            *(a + 14) ^= d; // D[2];

            d = *(c + 2) ^ ((*(c + 4) << 1) | (*(c + 4) >> 63));
            *(a + 15) ^= d; // D[3];
            *(a + 16) ^= d; // D[3];
            *(a + 17) ^= d; // D[3];
            *(a + 18) ^= d; // D[3];
            *(a + 19) ^= d; // D[3];

            d = *(c + 3) ^ ((*(c + 0) << 1) | (*(c + 0) >> 63));
            *(a + 20) ^= d; // D[4];
            *(a + 21) ^= d; // D[4];
            *(a + 22) ^= d; // D[4];
            *(a + 23) ^= d; // D[4];
            *(a + 24) ^= d; // D[4];
            

            //шаги ρ и π

            *(b +  0) =  *(a +  0);                             // rot(A[0, 0], r[0, 0]);
            *(b +  8) = (*(a +  1) << 36) | (*(a +  1) >> 28);  // rot(A[0, 1], r[0, 1]);
            *(b + 11) = (*(a +  2) <<  3) | (*(a +  2) >> 61);  // rot(A[0, 2], r[0, 2]);
            *(b + 19) = (*(a +  3) << 41) | (*(a +  3) >> 23);  // rot(A[0, 3], r[0, 3]);
            *(b + 22) = (*(a +  4) << 18) | (*(a +  4) >> 46);  // rot(A[0, 4], r[0, 4]);

            *(b +  2) = (*(a +  5) <<  1) | (*(a +  5) >> 63);  // rot(A[1, 0], r[1, 0]);
            *(b +  5) = (*(a +  6) << 44) | (*(a +  6) >> 20);  // rot(A[1, 1], r[1, 1]);
            *(b + 13) = (*(a +  7) << 10) | (*(a +  7) >> 54);  // rot(A[1, 2], r[1, 2]);
            *(b + 16) = (*(a +  8) << 45) | (*(a +  8) >> 19);  // rot(A[1, 3], r[1, 3]);
            *(b + 24) = (*(a +  9) <<  2) | (*(a +  9) >> 62);  // rot(A[1, 4], r[1, 4]);

            *(b +  4) = (*(a + 10) << 62) | (*(a + 10) >>  2);  // rot(A[2, 0], r[2, 0]);
            *(b +  7) = (*(a + 11) <<  6) | (*(a + 11) >> 58);  // rot(A[2, 1], r[2, 1]);
            *(b + 10) = (*(a + 12) << 43) | (*(a + 12) >> 21);  // rot(A[2, 2], r[2, 2]);
            *(b + 18) = (*(a + 13) << 15) | (*(a + 13) >> 49);  // rot(A[2, 3], r[2, 3]);
            *(b + 21) = (*(a + 14) << 61) | (*(a + 14) >>  3);  // rot(A[2, 4], r[2, 4]);

            *(b +  1) = (*(a + 15) << 28) | (*(a + 15) >> 36);  // rot(A[3, 0], r[3, 0]);
            *(b +  9) = (*(a + 16) << 55) | (*(a + 16) >>  9);  // rot(A[3, 1], r[3, 1]);
            *(b + 12) = (*(a + 17) << 25) | (*(a + 17) >> 39);  // rot(A[3, 2], r[3, 2]);
            *(b + 15) = (*(a + 18) << 21) | (*(a + 18) >> 43);  // rot(A[3, 3], r[3, 3]);
            *(b + 23) = (*(a + 19) << 56) | (*(a + 19) >>  8);  // rot(A[3, 4], r[3, 4]);

            *(b +  3) = (*(a + 20) << 27) | (*(a + 20) >> 37);  // rot(A[4, 0], r[4, 0]);
            *(b +  6) = (*(a + 21) << 20) | (*(a + 21) >> 44);  // rot(A[4, 1], r[4, 1]);
            *(b + 14) = (*(a + 22) << 39) | (*(a + 22) >> 25);  // rot(A[4, 2], r[4, 2]);
            *(b + 17) = (*(a + 23) <<  8) | (*(a + 23) >> 56);  // rot(A[4, 3], r[4, 3]);
            *(b + 20) = (*(a + 24) << 14) | (*(a + 24) >> 50);  // rot(A[4, 4], r[4, 4]);

            //шаг χ

            *(a +  0) = *(b +  0) ^ ((~*(b +  5)) & *(b + 10));
            *(a +  1) = *(b +  1) ^ ((~*(b +  6)) & *(b + 11));
            *(a +  2) = *(b +  2) ^ ((~*(b +  7)) & *(b + 12));
            *(a +  3) = *(b +  3) ^ ((~*(b +  8)) & *(b + 13));
            *(a +  4) = *(b +  4) ^ ((~*(b +  9)) & *(b + 14));

            *(a +  5) = *(b +  5) ^ ((~*(b + 10)) & *(b + 15));
            *(a +  6) = *(b +  6) ^ ((~*(b + 11)) & *(b + 16));
            *(a +  7) = *(b +  7) ^ ((~*(b + 12)) & *(b + 17));
            *(a +  8) = *(b +  8) ^ ((~*(b + 13)) & *(b + 18));
            *(a +  9) = *(b +  9) ^ ((~*(b + 14)) & *(b + 19));

            *(a + 10) = *(b + 10) ^ ((~*(b + 15)) & *(b + 20));
            *(a + 11) = *(b + 11) ^ ((~*(b + 16)) & *(b + 21));
            *(a + 12) = *(b + 12) ^ ((~*(b + 17)) & *(b + 22));
            *(a + 13) = *(b + 13) ^ ((~*(b + 18)) & *(b + 23));
            *(a + 14) = *(b + 14) ^ ((~*(b + 19)) & *(b + 24));

            *(a + 15) = *(b + 15) ^ ((~*(b + 20)) & *(b +  0));
            *(a + 16) = *(b + 16) ^ ((~*(b + 21)) & *(b +  1));
            *(a + 17) = *(b + 17) ^ ((~*(b + 22)) & *(b +  2));
            *(a + 18) = *(b + 18) ^ ((~*(b + 23)) & *(b +  3));
            *(a + 19) = *(b + 19) ^ ((~*(b + 24)) & *(b +  4));

            *(a + 20) = *(b + 20) ^ ((~*(b +  0)) & *(b +  5));
            *(a + 21) = *(b + 21) ^ ((~*(b +  1)) & *(b +  6));
            *(a + 22) = *(b + 22) ^ ((~*(b +  2)) & *(b +  7));
            *(a + 23) = *(b + 23) ^ ((~*(b +  3)) & *(b +  8));
            *(a + 24) = *(b + 24) ^ ((~*(b +  4)) & *(b +  9));

            //шаг ι - выполняется во внешнйе подпрограмме
        }

        // Полный keccak
        /// <summary>Все раунды keccak. a == S, c= C, b = B</summary>
        /// <param name="a">Зафиксированное внутреннее состояние S</param>
        /// <param name="c">Массив C (значения не важны)</param>
        /// <param name="b">Матрица B (значения не важны)</param>
        public unsafe void Keccackf(ulong * a, ulong * c, ulong * b)
        {
            roundB(a, c, b);
            //шаг ι
            *a ^= 0x0000000000000001;

            roundB(a, c, b); *a ^= 0x0000000000008082;
            roundB(a, c, b); *a ^= 0x800000000000808A;
            roundB(a, c, b); *a ^= 0x8000000080008000;

            roundB(a, c, b); *a ^= 0x000000000000808B;
            roundB(a, c, b); *a ^= 0x0000000080000001;
            roundB(a, c, b); *a ^= 0x8000000080008081;
            roundB(a, c, b); *a ^= 0x8000000000008009;

            roundB(a, c, b); *a ^= 0x000000000000008A;
            roundB(a, c, b); *a ^= 0x0000000000000088;
            roundB(a, c, b); *a ^= 0x0000000080008009;
            roundB(a, c, b); *a ^= 0x000000008000000A;

            roundB(a, c, b); *a ^= 0x000000008000808B;
            roundB(a, c, b); *a ^= 0x800000000000008B;
            roundB(a, c, b); *a ^= 0x8000000000008089;
            roundB(a, c, b); *a ^= 0x8000000000008003;

            roundB(a, c, b); *a ^= 0x8000000000008002;
            roundB(a, c, b); *a ^= 0x8000000000000080;
            roundB(a, c, b); *a ^= 0x000000000000800A;
            roundB(a, c, b); *a ^= 0x800000008000000A;

            roundB(a, c, b); *a ^= 0x8000000080008081;
            roundB(a, c, b); *a ^= 0x8000000000008080;
            roundB(a, c, b); *a ^= 0x0000000080000001;
            roundB(a, c, b); *a ^= 0x8000000080008008;
        }

        // keccak с неполными раундами
        /// <summary>Неполнораундовый keccack</summary>
        /// <param name="a">Внутреннее состояние S</param>
        /// <param name="c">Массив C (состояние не важно)</param>
        /// <param name="b">Матрица B (состояние не важно)</param>
        /// <param name="start">Начальный шаг, от нуля</param>
        /// <param name="count">Количество шагов (всего шагов столько, сколько констант в RC)</param>
        public unsafe void Keccack_i(ulong * a, ulong * c, ulong * b, int start, int count)
        {
            var end = start + count;
            for (int i = start; i < end; i++)
            {
                roundB(a, c, b); *a ^= RC[i];
            }
        }

        /// <summary>Ввод данных в состояние keccak. Предназначен только для версии 512 битов</summary>
        /// <param name="message">Указатель на очередную порцию данных</param>
        /// <param name="len">Количество байтов для записи (не более 72-х; константа r_512b)</param>
        /// <param name="S">Внутреннее состояние S</param>
        /// <param name="setPaddings">Если <see langword="true"/> - ввести padding в массив (при вычислении хеша делать на последнем блоке <= 71 байта)</param>
        // Сообщение P представляет собой массив элементов Pi,
        // каждый из которых в свою очередь является массивом 64-битных элементов
        public static unsafe void Keccak_Input_512(byte * message, byte len, byte * S, bool setPaddings = false)
        {
            if (len > r_512b || len < 0)
            {
                throw new ArgumentOutOfRangeException("len > r_512b || len < 0");
            }

            // В конце 72-хбайтового блока нужно поставить оконечный padding
            // Мы пропустили 8 ulong (64-ре байта), то есть 8-5=3 сейчас индекс у нас 3, но т.к. матрица транспонирована, то нам нужен не индекс [1, 3], а индекс [3, 1]
            // В индекс [3, 1] мы должны в старший байт записать 0x80. Значит, 3*5*8 + 1*8 + 7 = 135
            byte * es    = S + 135;
            byte * lastS = S;           // Если len = 0, то записываем в первый байт
            // Общий смысл инициализации
            // Массив информации в размере 72 байта записывается в начало состояния из 25-ти 8-мибайтовых слов; однако матрица S при этом имеет транспонированные индексы
            int i1 = 0, i2 = 0, i3 = 0, ss = S_len << 3;
            for (int i = 0; i < len; i++)
            {
                lastS = S + (i1 << 3) + i2*ss + i3;
                *lastS ^= *message;   // i2*ss - не ошибка, т.к. индексы в матрице транспонированны
                message++;

                // Выполняем приращения индексов в матрице
                i3++;
                if (i3 >= 8)
                {
                    i3 = 0;
                    i2++;   // Приращаем следующий индекс
                }

                if (i1 >= S_len)
                {
                    throw new Exception();
                }

                if (i2 >= S_len)
                {
                    i2 = S_len;
                    i2 = 0;
                    i1++;
                }

                // Это вычисление нужно для того, чтобы потом записать верно padding
                // Для len = 71 значение lastS должно совпасть с es
                lastS = S + (i1 << 3) + i2*ss + i3;
            }

            if (setPaddings)
            {
                if (len >= r_512b)
                    throw new ArgumentOutOfRangeException("len >= r_512b (must be < 72)");

                 *lastS ^= 0x01;
                 *es    ^= 0x80;
            }
        }

        /// <summary>Вывод данных из состояния keccak. Предназначен только для версии 512 битов</summary>
        /// <param name="output">Указатель на массив, готовый принять данные</param>
        /// <param name="len">Количество байтов для записи (не более 72-х; константа r_512b). Обычно используется 64 - это стойкость данного криптографического преобразования</param>
        /// <param name="S">Внутреннее состояние S</param>
        // Сообщение P представляет собой массив элементов Pi,
        // каждый из которых в свою очередь является массивом 64-битных элементов
        public static unsafe void Keccak_Output_512(byte * output, byte len, byte * S)
        {
            if (len > r_512b || len < 0)
            {
                throw new ArgumentOutOfRangeException("len > r_512b || len < 0");
            }

            // Матрица S - это матрица 5x5 по 8 байтов. Мы проходим по первому столбцу, и собираем оттуда данные
            // Потом - по второму столбцу, и собираем оттуда данные
            for (int i = 0; i < 40;  i += 8)  // 40 = 8*5
            for (int j = 0; j < 200; j += 40) // 200 = 40*5
            for (int k = 0; k < 8; k++)
            {
                if (len == 0)
                    goto End;
                
                *output = *(S + i + j + k);

                output++;
            }

            End: ;
        }
    }
}
