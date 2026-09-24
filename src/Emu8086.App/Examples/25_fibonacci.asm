; The first 15 Fibonacci numbers.
include 'emu8086.inc'
#make_COM#
org 100h

    mov cx, 15
    mov ax, 0
    mov bx, 1
next:
    call PRINT_NUM_UNS
    PUTC ' '
    mov dx, ax
    add dx, bx              ; next = a + b
    mov ax, bx
    mov bx, dx
    loop next
    ret

DEFINE_PRINT_NUM_UNS
END
