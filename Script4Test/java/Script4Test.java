import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

// Только тело метода становится блоками; шапка файла переносится как есть.
// Внутри — исключительно письменное подмножество NSG: объявления, присваивания,
// while/if, арифметика и вызовы. Дженерики живут в ТЕКСТОВЫХ слотах (тип),
// поэтому они не «сырой фрагмент», а полноценный блок.
public class Script4Test {

    public int crunch(int n) {
        List<Integer> sieve = new ArrayList<Integer>();
        Map<String, Integer> tally = new HashMap<String, Integer>();
        int primes = 0;
        int i = 0;
        while (i <= n) {
            sieve.add(0);
            i++;
        }
        i = 2;
        while (i <= n) {
            if (sieve.get(i) == 0) {
                primes += 1;
                tally.put("p" + primes, i);
                int j = i * i;
                while (j <= n) {
                    sieve.set(j, 1);
                    j += i;
                }
            }
            i++;
        }
        int mask = (1 << 8) - 1;
        int folded = (primes & mask) ^ (primes >> 3);
        int bonus = folded > 0 ? folded : -1;
        return primes + bonus + tally.size();
    }
}
