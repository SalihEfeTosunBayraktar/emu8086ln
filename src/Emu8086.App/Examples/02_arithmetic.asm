; Arithmetic: ADD, SUB, MUL, DIV and printing numbers with emu8086.inc.
include 'emu8086.inc'
#make_COM#
org 100h

    mov ax, 25
    add ax, 17              ; AX = 42
    PRINT '25 + 17 = '
    call PRINT_NUM
    PRINTN ''

    mov ax, 10
    sub ax, 35              ; AX = -25 (signed)
    PRINT '10 - 35 = '
    call PRINT_NUM
    PRINTN ''

    mov ax, 123
    mov bx, 45
    mul bx                  ; DX:AX = AX * BX
    PRINT '123 * 45 = '
    call PRINT_NUM_UNS
    PRINTN ''

    mov ax, 1000
    mov dx, 0               ; DX:AX is the dividend
    mov bx, 7
    div bx                  ; AX = quotient, DX = remainder
    PRINT '1000 / 7 = '
    call PRINT_NUM_UNS
    PRINT ' remainder '
    mov ax, dx
    call PRINT_NUM_UNS
    PRINTN ''
    ret

DEFINE_PRINT_NUM
DEFINE_PRINT_NUM_UNS
END
