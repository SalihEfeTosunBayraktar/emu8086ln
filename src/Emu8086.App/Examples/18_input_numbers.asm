; Read two numbers from the keyboard and print their sum (emu8086.inc).
include 'emu8086.inc'
#make_COM#
org 100h

    PRINT 'First number: '
    call SCAN_NUM
    mov bx, cx
    PRINTN ''
    PRINT 'Second number: '
    call SCAN_NUM
    PRINTN ''
    mov ax, bx
    add ax, cx
    PRINT 'Sum: '
    call PRINT_NUM
    ret

DEFINE_SCAN_NUM
DEFINE_PRINT_NUM
DEFINE_PRINT_NUM_UNS
END
