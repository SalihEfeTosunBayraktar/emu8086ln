; Bubble sort an array of bytes, then print it.
include 'emu8086.inc'
#make_COM#
org 100h

    mov cx, count - 1       ; passes
outer:
    push cx
    mov si, 0
inner:
    mov al, numbers[si]
    cmp al, numbers[si+1]
    jbe no_swap
    xchg al, numbers[si+1]  ; swap neighbours
    mov numbers[si], al
no_swap:
    inc si
    loop inner
    pop cx
    loop outer

    mov si, 0
print:
    mov al, numbers[si]
    mov ah, 0
    call PRINT_NUM_UNS
    PUTC ' '
    inc si
    cmp si, count
    jb print
    ret

numbers db 42, 7, 99, 3, 58, 21, 1, 77
count   equ $ - numbers

DEFINE_PRINT_NUM_UNS
END
