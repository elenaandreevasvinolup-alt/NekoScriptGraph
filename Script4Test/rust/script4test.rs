// Только тело функции становится блоками; шапка переносится как есть.
// Внутри — письменное подмножество NSG.
fn crunch(n: i32) -> i32 {
    let mut primes: i32 = 0;
    let mut i: i32 = 2;
    while i <= n {
        let mut is_prime: i32 = 1;
        let mut j: i32 = 2;
        while j * j <= i {
            if i % j == 0 {
                is_prime = 0;
            }
            j += 1;
        }
        primes += is_prime;
        i += 1;
    }
    let mask: i32 = (1 << 8) - 1;
    let folded: i32 = (primes & mask) ^ (primes >> 3);
    let bonus: i32 = if folded > 0 { folded } else { -1 };
    return primes + bonus;
}
