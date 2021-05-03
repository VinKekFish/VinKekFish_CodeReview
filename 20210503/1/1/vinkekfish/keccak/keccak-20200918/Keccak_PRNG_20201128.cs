﻿using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using cryptoprime;
using static cryptoprime.keccak;
using static cryptoprime.BytesBuilderForPointers;

namespace vinkekfish.keccak.keccak_20200918
{
    public unsafe class Keccak_PRNG_20201128 : Keccak_base_20200918
    {
        public readonly AllocatorForUnsafeMemoryInterface allocator             = new BytesBuilderForPointers.AllocHGlobal_AllocatorForUnsafeMemory();
        public          AllocatorForUnsafeMemoryInterface allocatorForSaveBytes = new BytesBuilderForPointers.AllocHGlobal_AllocatorForUnsafeMemory(); // new BytesBuilderForPointers.Fixed_AllocatorForUnsafeMemory();
        // Fixed работает раза в 3 медленнее почему-то

        public Keccak_PRNG_20201128(AllocHGlobal_AllocatorForUnsafeMemory allocator = null)
        {
            if (allocator != null)
                this.allocator = allocator;

            inputTo = AllocMemory(InputSize);
        }

        public Record AllocMemory(long len)
        {
            return allocator.AllocMemory(len);
        }

        // TODO: сделать тесты на Clone
        public override Keccak_abstract Clone()
        {
            var result = new Keccak_PRNG_20201128();

            // Очищаем C и B, чтобы не копировать какие-то значения, которые не стоит копировать, да и хранить тоже
            clearOnly_C_and_B();

            // Копировать всё состояние не обязательно. Но здесь, для надёжности, копируется всё (в т.ч. ранее очищенные нули)
            for (int i = 0; i < State.LongLength; i++)
                    result.State[i] = State[i];

            return result;
        }


        /// <summary>Сюда можно добавлять байты для ввода</summary>
        protected readonly BytesBuilderForPointers INPUT = new BytesBuilderForPointers(); // Не забыт ли вызов InputBytesImmediately при добавлении сюда?
        public    const    int InputSize = 64;

        /// <summary>Это массив для немедленного введения в Sponge на следующем шаге</summary>
        protected readonly Record inputTo;
        protected          bool   inputReady  = false;

        /// <summary>Ввести рандомизирующие байты (в том числе, открытый вектор инициализации). Не выполняет криптографических операций</summary>
        /// <param name="bytesToInput">Рандомизирующие байты. Копируются. bytesToInput должны быть очищены вручную</param>
        public void InputBytes(byte[] bytesToInput)
        {
            INPUT.add(BytesBuilderForPointers.CloneBytes(bytesToInput, allocator));
            InputBytesImmediately();
        }

        /// <summary>Ввести рандомизирующие байты (в том числе, открытый вектор инициализации). Не выполняет криптографических операций</summary>
        /// <param name="bytesToInput">Рандомизирующие байты. Копируются. bytesToInput должны быть очищены вручную</param>
        /// <param name="len">Длина рандомизирующей последовательности</param>
        public void InputBytes(byte * bytesToInput, long len)
        {
            INPUT.add(BytesBuilderForPointers.CloneBytes(bytesToInput, 0, len, allocator));
            InputBytesImmediately();
        }

        /// <summary>Ввести секретный ключ (вместе с криптографическим преобразованием и отбоем в режиме OVERWITE)</summary>
        /// <param name="key">Ключ, кратный 64-ём байтам</param>
        public void InputKeyAndStep(byte * key, long key_length)
        {
            if (INPUT.countOfBlocks > 0)
                throw new ArgumentException("key must be input before the generation or input an initialization vector (or see InputKeyAndStep code)", "key");

            INPUT.add(key, key_length);
            InputBytesImmediately();
            do
            {
                calcStep(Overwrite: false);
                InputBytesImmediately();
            }
            while (inputReady);

            // Завершаем ввод ключа конструкцией Overwrite, которая даёт некую необратимость состояния в отношении ключа
            inputTo.Clear();
            inputReady = true;
            calcStep(Overwrite: true);

            if (INPUT.countOfBlocks > 0)
            {
                INPUT.clear();
                Clear(true);
                throw new ArgumentException("key must be a multiple of 64 bytes", "key");
            }
        }

        public override void Clear(bool GcCollect = false)
        {
            inputTo.Clear();

            INPUT  .clear();
            output .clear();

            inputReady = false;

            base.Clear(GcCollect);
        }

        public override void Dispose(bool disposing)
        {
            inputTo?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Переносит байты из очереди ожидания в массив байтов для непосредственного ввода в криптографическое состояние. Не выполняет криптографических операций</summary>
        protected void InputBytesImmediately()
        {
            if (!inputReady)
            if (INPUT.Count >= InputSize)
            {
                // TODO: сделать тесты на верность getBytesAndRemoveIt и, по возможности, на его использование
                INPUT.getBytesAndRemoveIt(inputTo);
                inputReady = true;
            }
        }

        /// <summary>Выполняет шаг keccak и сохраняет полученный результат в output</summary>
        public void calcStepAndSaveBytes()
        {
            calcStep(SaveBytes: true);
        }

        /// <summary>Расчитывает шаг губки keccak. Если есть InputSize (64) байта для ввода (точнее, inputReady == true), то вводит первые 64-ре байта</summary>
        /// <param name="SaveBytes">Если <see langword="null"/>, выход не сохраняется</param>
        /// <param name="Overwrite">Если <see langword="true"/>, то вместо xor применяет перезапись внешней части состояния на вводе данных (конструкция Overwrite)</param>
        // TODO: Разобраться с тем, что состояние не зафиксировано в памяти, а может перемещаться
        public void calcStep(bool SaveBytes = false, bool Overwrite = false)
        {
            Keccak_abstract.KeccakStatesArray.getStatesArray(out GCHandle handle, this.State, out byte * S, out byte * B, out byte * C, out byte * Base, out ulong * Slong, out ulong * Blong, out ulong * Clong);
            try
            {
                // InputBytesImmediately();    // Это на всякий случай добавлено
                if (inputReady)
                {
                    byte * input = inputTo.array;

                    if (Overwrite)
                        Keccak_InputOverwrite64_512(message: input, len: InputSize, S: S);
                    else
                        Keccak_Input_512(message: input, len: InputSize, S: S);

                    inputReady = false;
                    InputBytesImmediately();
                }
                
                Keccackf(a: Slong, c: Clong, b: Blong);

                if (SaveBytes)
                {
                    var result = allocatorForSaveBytes.AllocMemory(InputSize);
                    Keccak_Output_512(output: result.array, len: InputSize, S: S);

                    output.add(result);
                }
            }
            finally
            {
                Keccak_abstract.KeccakStatesArray.handleFree(handle);
            }
        }

        public readonly BytesBuilderForPointers output = new BytesBuilderForPointers();

        /// <summary>Количество элементов, которые доступны для вывода без применения криптографических операций</summary>
        public long outputCount { get => output.Count; }

        /// <summary>Выдаёт случайные криптостойкие значения байтов. Выгодно использовать при большом количестве байтов (64 и более). Выполняет криптографические операции, если байтов не хватает</summary>
        /// <param name="output">Массив, в который записывается результат</param>
        /// <param name="len">Количество байтов, которые необходимо записать. Используйте outputCount, чтобы узнать, сколько байтов уже готово к выводу (без выполнения криптографических операций)</param>
        public void getBytes(Record outputRecord, long len)
        {
            var output = outputRecord.array;

            // Проверяем уже готовые байты
            if (this.output.Count > 0)
            {
                var readyLen = this.output.Count;
                if (readyLen > len)
                {
                    readyLen = len;
                }

                using var b = this.output.getBytesAndRemoveIt(  AllocMemory(readyLen)  );

                BytesBuilder.CopyTo(b.len, readyLen, b.array, output);

                output += readyLen;
                len    -= readyLen;

                if (len <= 0)
                    return;
            }

            // Если готовых байтов нет, то начинаем вычислять те, что ещё не готовы
            // И сразу же их записываем
            Keccak_abstract.KeccakStatesArray.getStatesArray(out GCHandle handle, this.State, out byte * S, out _, out _, out _, out _, out _, out _);
            try
            {
                while (len > 0)
                {
                    calcStep();
                    Keccak_Output_512(output: output, len: (byte) (len >= 64 ? 64 : len), S: S);
                    len    -= 64;
                    output += 64;
                }
            }
            finally
            {
                Keccak_abstract.KeccakStatesArray.handleFree(handle);
            }
        }
        
        public byte getByte()
        {
            if (this.output.Count <= 0)
            {
                calcStepAndSaveBytes();
            }

            using var b = output.getBytesAndRemoveIt(  AllocMemory(1)  );

            var result = b.array[0];

            return result;
        }

        /// <summary>Выдаёт случайное криптостойкое число от 0 до cutoff включительно. Это вспомогательная функция для основной функции генерации случайных чисел</summary>
        /// <param name="cutoff">Максимальное число (включительно) для генерации. cutoff должен быть близок к ulong.MaxValue или к 0x8000_0000__0000_0000U, иначе неопределённая отсрочка будет очень долгой</param>
        /// <returns>Случайное число в диапазоне [0; cutoff]</returns>
        public ulong getUnsignedInteger(ulong cutoff = ulong.MaxValue, Record arrayAt8Length = null)
        {
            var b = arrayAt8Length ?? AllocMemory(8);
            try
            {
                while (true)
                {
                    if (this.output.Count < 8)
                    {
                        calcStepAndSaveBytes();
                    }
                    
                    output.getBytesAndRemoveIt(b);

                    BytesBuilderForPointers.BytesToULong(out ulong result, b.array, 0, b.len);

                    if (cutoff < 0x8000_0000__0000_0000U)
                        result &= 0x7FFF_FFFF__FFFF_FFFFU;  // Сбрасываем старший бит, т.к. он не нужен никогда

                    if (result <= cutoff)
                        return result;
                }
            }
            finally
            {
                if (arrayAt8Length == null)
                    b.Dispose();
            }
            
        }

        /// <summary>Получает случайное значение в диапазоне, указанном в функции getCutoffForUnsignedInteger</summary>
        /// <param name="min">Минимальное значение</param>
        /// <param name="cutoff">Результат функции getCutoffForUnsignedInteger</param>
        /// <param name="range">Результат функции getCutoffForUnsignedInteger</param>
        /// <returns>Случайное число в указанном диапазоне</returns>
        public ulong getUnsignedInteger(ulong min, ulong cutoff, ulong range, Record arrayAt8Length = null)
        {
            var random = getUnsignedInteger(cutoff, arrayAt8Length) % range;

            return random + min;
        }

        /// <summary>Вычисляет параметры для применения в getUnsignedInteger</summary>
        /// <param name="min">Минимальное значение для генерации</param>
        /// <param name="max">Максимальное значнеие для генерации (включительно)</param>
        /// <param name="cutoff">Параметр cutoff для передачи getUnsignedInteger</param>
        // TODO: хорошо протестировать
        public void getCutoffForUnsignedInteger(ulong min, ulong max, out ulong cutoff, out ulong range)
        {
            range = max - min + 1;

            if (range >= 0x8000_0000__0000_0000U)
            {
                cutoff = range;
                return;
            }

            var mod = (0x8000_0000__0000_0000U) % range;

            if (mod == 0)
            {
                cutoff = 0x8000_0000__0000_0000U;
                return;
            }

            var result = 0x8000_0000__0000_0000U - mod;

            if (result % range != 0)
                throw new Exception("Fatal error: Keccak_PRNG_20201128.getCutoffForUnsignedInteger");

            cutoff = result;
        }

        /// <summary>Осуществляет перестановки таблицы 2-хбайтовых целых чисел</summary>
        /// <param name="table">Исходная таблица для перестановок длиной не более int.MaxValue</param>
        public void doRandomPermutationForUShorts(ushort[] table)
        {
            // Иначе всё равно будет слишком долго
            if (table.LongLength > int.MaxValue)
                throw new ArgumentException("doRandomCubicPermutationForUShorts: table is very long");
            if (table.Length <= 3)
                throw new ArgumentException("doRandomCubicPermutationForUShorts: table is very short");

            var len = (ulong) table.LongLength;

            // Алгоритм тасования Дурштенфельда
            // https://ru.wikipedia.org/wiki/Тасование_Фишера_—_Йетса
            using var b8 = allocator.AllocMemory(8);
            for (ulong i = 0; i < len - 1; i++)
            {
                getCutoffForUnsignedInteger(0, (ulong) len - i - 1, out ulong cutoff, out ulong range);
                var index = getUnsignedInteger(0, cutoff, range, b8) + i;

                do2Permutation(i, index);
            }

            void do2Permutation(ulong i1, ulong i2)
            {
                var a     = table[i1];
                table[i1] = table[i2];
                table[i2] = a;
            }
            /*
            void do3Permutation(int i1, int i2, int i3)
            {
                var a1    = table[i1];
                var a2    = table[i2];
                var a3    = table[i3];

                table[i1] = a2;
                table[i2] = a3;
                table[i3] = a1;
            }*/
        }
    }
}
